using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreShared.Events;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Models;
#if NET9_0_OR_GREATER
using Ivi.Visa;
#endif

namespace DeviceCommunication.Infrastructure
{
    internal sealed class UsbVisaCommPort : ICommPort, IDisposable
    {
        /// <summary>掉线后冷却期内不再疯狂重连，避免每秒触发 VISA 异常。</summary>
        private const int ReconnectCooldownMs = 5000;

        private readonly UsbConfiguration _configuration;
        private readonly string? _resourceString;
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);
        private IUsbVisaSessionBridge? _session;
        private bool _linkLost;
        private long _nextReconnectAttemptMs;

        public UsbVisaCommPort(UsbConfiguration configuration, string? resourceString = null)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _resourceString = string.IsNullOrWhiteSpace(resourceString) ? configuration.ResourceString : resourceString;
        }

        public bool IsOpen => _session?.IsOpen == true;

        public Action<byte[], object>? OnDataReceived { get; set; }
        public event EventHandler<ExceptionEvent>? OnException;
        public event EventHandler<ExecuteLogEvent>? OnExecuteLog;

        public bool Initialize() => true;

        public bool Open()
        {
            if (_linkLost && GetMonotonicMs() < _nextReconnectAttemptMs)
                return false;

            if (!VisaRuntime.TryEnsureAvailable(out var visaError))
            {
                OnException?.Invoke(this, new ExceptionEvent
                {
                    ClassName = nameof(UsbVisaCommPort),
                    MethodName = nameof(Open),
                    ErrorMessage = visaError
                });
                return false;
            }

            try
            {
                if (_session?.IsOpen == true)
                {
                    _linkLost = false;
                    return true;
                }

                _session ??= new UsbVisaSessionHandle();
                var resourceName = BuildResourceString();
                if (!_session.TryOpen(resourceName, _configuration.TimeoutMs, out var error))
                {
                    MarkLinkLost(notify: false);
                    OnException?.Invoke(this, new ExceptionEvent
                    {
                        ClassName = nameof(UsbVisaCommPort),
                        MethodName = nameof(Open),
                        ErrorMessage = error ?? "USB VISA resource could not be opened."
                    });
                    return false;
                }

                // 仅配置终止符，不要在 Open 时用 Read 清空缓冲区（空缓冲 Read 会触发协议错误导致“连不上”）。
                ConfigureSessionTermination();
                _linkLost = false;

                OnExecuteLog?.Invoke(this, new ExecuteLogEvent
                {
                    ClassName = nameof(UsbVisaCommPort),
                    MethodName = nameof(Open),
                    LogMessage = $"Opened USB VISA resource {resourceName}"
                });
                return true;
            }
            catch (Exception ex)
            {
                MarkLinkLost(notify: false);
                OnException?.Invoke(this, new ExceptionEvent
                {
                    ClassName = nameof(UsbVisaCommPort),
                    MethodName = nameof(Open),
                    ErrorMessage = ex.Message
                });
                return false;
            }
        }

        public void Close()
        {
            // 先等在途 IO 结束，再释放会话，避免退出时 Task.Run 里访问已被置空的 _session。
            var lockTaken = false;
            try
            {
                lockTaken = _ioLock.Wait(2000);
                SafeInvalidateSession();
                _linkLost = false;
                _nextReconnectAttemptMs = 0;
            }
            catch (Exception ex)
            {
                OnException?.Invoke(this, new ExceptionEvent
                {
                    ClassName = nameof(UsbVisaCommPort),
                    MethodName = nameof(Close),
                    ErrorMessage = ex.Message
                });
            }
            finally
            {
                if (lockTaken)
                {
                    try { _ioLock.Release(); }
                    catch (ObjectDisposedException) { }
                    catch (SemaphoreFullException) { }
                }
            }
        }

        public bool SendBytes(byte[] data)
        {
            try
            {
                if (!TryEnsureOpen())
                    return false;
                _session!.WriteBytes(data);
                return true;
            }
            catch (Exception ex)
            {
                HandleIoFailure(nameof(SendBytes), ex);
                return false;
            }
        }

        public bool SendAsciiString(string data)
        {
            try
            {
                if (!TryEnsureOpen())
                    return false;
                _session!.WriteString(ApplyWriteTermination(data));
                return true;
            }
            catch (Exception ex)
            {
                HandleIoFailure(nameof(SendAsciiString), ex);
                return false;
            }
        }

        public async Task SendBytesAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!SendBytes(data))
                    throw new InvalidOperationException("USB VISA send failed.");
            }, cancellationToken).ConfigureAwait(false);
        }

        public async Task<byte[]> SendAndReceiveAsync(byte[] data, int timeoutMs = 5000,
            CancellationToken cancellationToken = default)
        {
            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!TryEnsureOpen())
                    return Array.Empty<byte>();

                var session = _session ?? throw new ObjectDisposedException(nameof(UsbVisaCommPort));
                session.SetTimeout(timeoutMs);
                return await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        session.WriteBytes(data);
                        var response = session.ReadBytes();
                        OnDataReceived?.Invoke(response, this);
                        return response;
                    }
                    catch (Exception ex) when (IsDisconnected(Unwrap(ex)) || !session.IsOpen)
                    {
                        MarkLinkLost(notify: true);
                        return Array.Empty<byte>();
                    }
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                HandleIoFailure(nameof(SendAndReceiveAsync), ex);
                return Array.Empty<byte>();
            }
            finally
            {
                try { _ioLock.Release(); }
                catch (ObjectDisposedException) { }
            }
        }

        public async Task<string> SendAndReceiveStringAsync(string data, int timeoutMs = 5000,
            CancellationToken cancellationToken = default)
        {
            await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // 掉线后软失败：返回空串，不把 IOException 抛给轮询线程 / 调试器。
                if (!TryEnsureOpen())
                    return string.Empty;

                var session = _session ?? throw new ObjectDisposedException(nameof(UsbVisaCommPort));
                session.SetTimeout(timeoutMs);
                return await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!session.TryWriteAndReadString(ApplyWriteTermination(data), out var response))
                    {
                        MarkLinkLost(notify: true);
                        return string.Empty;
                    }

                    OnDataReceived?.Invoke(Encoding.ASCII.GetBytes(response), this);
                    return response;
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (ex.GetType().Name == "IOTimeoutException")
                    throw;

                HandleIoFailure(nameof(SendAndReceiveStringAsync), ex);
                return string.Empty;
            }
            finally
            {
                try { _ioLock.Release(); }
                catch (ObjectDisposedException) { }
            }
        }

        public Task<string> SendAndReceiveStringAsync(string data, Func<string, bool> isComplete,
            int timeoutMs = 5000, CancellationToken cancellationToken = default)
        {
            if (isComplete is null) throw new ArgumentNullException(nameof(isComplete));
            return SendAndReceiveStringAsync(data, timeoutMs, cancellationToken);
        }

        public void Dispose()
        {
            Close();
            try { _ioLock.Dispose(); }
            catch (ObjectDisposedException) { }
        }

        private bool TryEnsureOpen()
        {
            if (IsOpen)
                return true;
            return Open();
        }

        /// <summary>记录异常；USB 掉线时关闭会话并进入重连冷却。</summary>
        private void HandleIoFailure(string methodName, Exception ex)
        {
            var root = Unwrap(ex);
            var disconnected = IsDisconnected(root) || !IsOpen;
            if (disconnected)
                MarkLinkLost(notify: false);

            OnException?.Invoke(this, new ExceptionEvent
            {
                ClassName = nameof(UsbVisaCommPort),
                MethodName = methodName,
                ErrorMessage = disconnected
                    ? "USB 设备已断开连接，会话已关闭。"
                    : root.Message
            });
        }

        private void MarkLinkLost(bool notify)
        {
            _linkLost = true;
            _nextReconnectAttemptMs = GetMonotonicMs() + ReconnectCooldownMs;
            SafeInvalidateSession();

            if (notify)
            {
                OnException?.Invoke(this, new ExceptionEvent
                {
                    ClassName = nameof(UsbVisaCommPort),
                    MethodName = nameof(MarkLinkLost),
                    ErrorMessage = "USB 设备已断开连接，会话已关闭。"
                });
            }
        }

        private void SafeInvalidateSession()
        {
            try
            {
                _session?.Dispose();
            }
            catch
            {
                // ignore
            }
            finally
            {
                _session = null;
            }
        }

        private static Exception Unwrap(Exception ex)
        {
            while (ex is AggregateException { InnerExceptions.Count: 1, InnerException: { } inner })
                ex = inner;
            return ex;
        }

        private static bool IsDisconnected(Exception ex)
        {
            if (ex is IOException && ex.Message.Contains("USB VISA", StringComparison.Ordinal))
                return true;

#if NET9_0_OR_GREATER
            if (ex is NativeVisaException native)
                return UsbVisaSessionHandle.IsLinkLost(native);
#endif
            return false;
        }

        private static long GetMonotonicMs()
        {
#if NET9_0_OR_GREATER
            return Environment.TickCount64;
#else
            return Environment.TickCount & int.MaxValue;
#endif
        }

        private string ApplyWriteTermination(string data)
        {
            var termination = _configuration.WriteTermination;
            if (string.IsNullOrEmpty(termination) || data.EndsWith(termination, StringComparison.Ordinal))
                return data;

            return data + termination;
        }

        private void ConfigureSessionTermination()
        {
            var terminationChar = ResolveTerminationChar(_configuration.ReadTermination);
            _session!.ConfigureReadTermination(terminationChar, _configuration.EnableReadTermination);
        }

        private static byte ResolveTerminationChar(string termination)
        {
            if (string.IsNullOrEmpty(termination))
                return (byte)'\n';

            return (byte)termination[^1];
        }

        private string BuildResourceString()
        {
            if (!string.IsNullOrWhiteSpace(_resourceString))
                return _resourceString!;

            if (string.IsNullOrWhiteSpace(_configuration.VendorId))
                throw new InvalidOperationException("USB VendorId is required when ResourceString is not set.");

            if (string.IsNullOrWhiteSpace(_configuration.ProductId))
                throw new InvalidOperationException("USB ProductId is required when ResourceString is not set.");

            var vendorId = NormalizeHex(_configuration.VendorId);
            var productId = NormalizeHex(_configuration.ProductId);
            var serial = string.IsNullOrWhiteSpace(_configuration.SerialNumber) ? "0" : _configuration.SerialNumber;
            return $"USB0::{vendorId}::{productId}::{serial}::INSTR";
        }

        private static string NormalizeHex(string value)
        {
            var trimmed = value.Trim();
            return trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? trimmed : $"0x{trimmed}";
        }
    }
}
