using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreShared.Events;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Models;
using HPSocket;
using HPSocket.Sdk;
using HPSocket.Tcp;
using ITcpClient = DeviceCommunication.Abstractions.ITcpClient;

namespace DeviceCommunication.Protocols.Tcp
{
    internal class TcpClient : ITcpClient
    {
        private const int DefaultConnectTimeoutMs = 3000;

        public Action<byte[], object>? OnDataReceived { get; set; }
        public Action<object>? OnConnected { get; set; }
        public event EventHandler<ExceptionEvent>? OnException;
        public event EventHandler<ExecuteLogEvent>? OnExecuteLog;
        protected HPSocket.Tcp.TcpClient _tcpClient = null!;
        public bool IsConnected => _tcpClient?.IsConnected ?? false;
        public bool IsOpen => _tcpClient?.IsConnected ?? false;
        public NetConfiguration? Configuration { get; set; }
        public string? IpAddress { get; set; }
        public int Port { get; set; }

        private readonly object _receiveLock = new();
        private readonly MemoryStream _receiveBuffer = new();
        private TaskCompletionSource<bool>? _dataAvailableTcs;

        public TcpClient(NetConfiguration? configuration)
        {
            Configuration = configuration;
        }

        public bool Initialize()
        {
            try
            {
                if (Configuration is null)
                    return false;

                IpAddress = Configuration.IpAddress!;
                Port = Configuration.Port;
                _tcpClient = new HPSocket.Tcp.TcpClient();
                _tcpClient.Address = IpAddress;
                _tcpClient.Port = (ushort)Port;
                _tcpClient.OnClose += OnClose;
                _tcpClient.OnReceive += OnReceive;
                _tcpClient.OnConnect += OnConnect;
                _tcpClient.SocketBufferSize = 1024 * 1024;
                return true;
            }
            catch (Exception e)
            {
                OnException?.Invoke(this,
                    new ExceptionEvent
                    {
                        ErrorMessage = e.Message,
                        ClassName = nameof(TcpClient),
                        MethodName = nameof(Initialize)
                    });
                return false;
            }
        }

        private HandleResult OnConnect(IClient sender)
        {
            OnExecuteLog?.Invoke(this,
                new ExecuteLogEvent
                {
                    LogMessage = $"TCP client connected to server {IpAddress}:{Port}",
                    ClassName = nameof(TcpClient),
                    MethodName = nameof(OnConnect)
                });
            OnConnected?.Invoke(this);
            return HandleResult.Ok;
        }

        private HandleResult OnReceive(IClient sender, byte[] data)
        {
            if (data is { Length: > 0 })
            {
                lock (_receiveLock)
                {
                    _receiveBuffer.Write(data, 0, data.Length);
                    _dataAvailableTcs?.TrySetResult(true);
                }
            }

            OnDataReceived?.Invoke(data, IpAddress!);
            return HandleResult.Ok;
        }

        private HandleResult OnClose(IClient sender, SocketOperation enOperation, int errorCode)
        {
            OnExecuteLog?.Invoke(this,
                new ExecuteLogEvent
                {
                    LogMessage = $"TCP client disconnected. Operation: {enOperation}, ErrorCode: {errorCode}",
                    ClassName = nameof(TcpClient),
                    MethodName = nameof(OnClose)
                });
            return HandleResult.Ok;
        }

        /// <summary>
        /// 连接服务器（等待连接建立）
        /// </summary>
        public bool Open()
        {
            try
            {
                if (_tcpClient is null)
                    return false;

                if (_tcpClient.IsConnected)
                    return true;

                OnExecuteLog?.Invoke(this,
                    new ExecuteLogEvent
                    {
                        ClassName = nameof(TcpClient),
                        MethodName = nameof(Open),
                        LogMessage = $"Attempting to connect to TCP server at {IpAddress}:{Port}"
                    });

                if (!_tcpClient.Connect())
                    return false;

                var deadline = DateTime.UtcNow.AddMilliseconds(DefaultConnectTimeoutMs);
                while (!_tcpClient.IsConnected && DateTime.UtcNow < deadline)
                    Thread.Sleep(10);

                return _tcpClient.IsConnected;
            }
            catch (Exception e)
            {
                OnException?.Invoke(this,
                    new ExceptionEvent
                    {
                        ErrorMessage = e.Message,
                        ClassName = nameof(TcpClient),
                        MethodName = nameof(Open)
                    });
                return false;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Close()
        {
            try
            {
                ClearReceiveBuffer();
                _tcpClient?.Stop();
                OnExecuteLog?.Invoke(this,
                    new ExecuteLogEvent
                    {
                        ClassName = nameof(TcpClient),
                        MethodName = nameof(Close),
                        LogMessage = $"Disconnected from TCP server at {IpAddress}:{Port}"
                    });
            }
            catch (Exception e)
            {
                OnException?.Invoke(this,
                    new ExceptionEvent
                    {
                        ErrorMessage = e.Message,
                        ClassName = nameof(TcpClient),
                        MethodName = nameof(Close)
                    });
            }
        }

        public bool SendBytes(byte[] data)
        {
            try
            {
                return _tcpClient?.Send(data, data.Length) ?? false;
            }
            catch (Exception e)
            {
                OnException?.Invoke(this,
                    new ExceptionEvent
                    {
                        ErrorMessage = e.Message,
                        ClassName = nameof(TcpClient),
                        MethodName = nameof(SendBytes)
                    });
                return false;
            }
        }

        public bool SendAsciiString(string data)
        {
            try
            {
                var buffer = Encoding.ASCII.GetBytes(data);
                return SendBytes(buffer);
            }
            catch (Exception e)
            {
                OnException?.Invoke(this,
                    new ExceptionEvent
                    {
                        ErrorMessage = e.Message,
                        ClassName = nameof(TcpClient),
                        MethodName = nameof(SendAsciiString)
                    });
                return false;
            }
        }

        public Task SendBytesAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _tcpClient?.Send(data, data.Length);
            }
            catch (Exception e)
            {
                OnException?.Invoke(this,
                    new ExceptionEvent
                    {
                        ClassName = nameof(TcpClient),
                        MethodName = nameof(SendBytesAsync),
                        ErrorMessage = e.Message
                    });
            }

            return Task.CompletedTask;
        }

        public async Task<byte[]> ReceiveAsync(int byteCount, CancellationToken cancellationToken = default)
        {
            if (byteCount <= 0)
                return Array.Empty<byte>();

            using var responseStream = new MemoryStream(byteCount);
            var deadline = DateTime.UtcNow.AddMilliseconds(DefaultConnectTimeoutMs);

            while (responseStream.Length < byteCount && DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = ReadAvailableBytes(byteCount - (int)responseStream.Length);
                if (chunk.Length > 0)
                {
                    responseStream.Write(chunk, 0, chunk.Length);
                    continue;
                }

                await WaitForDataAsync(50, cancellationToken).ConfigureAwait(false);
            }

            if (responseStream.Length < byteCount)
                throw new TimeoutException($"Expected {byteCount} bytes, received {responseStream.Length}");

            return responseStream.ToArray();
        }

        public async Task<byte[]> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            var idleTimeout = 200;
            var totalDeadline = DateTime.UtcNow.AddMilliseconds(DefaultConnectTimeoutMs);
            using var responseStream = new MemoryStream();

            var dataReceived = false;
            while (DateTime.UtcNow < totalDeadline && !cancellationToken.IsCancellationRequested)
            {
                var chunk = ReadAvailableBytes();
                if (chunk.Length > 0)
                {
                    responseStream.Write(chunk, 0, chunk.Length);
                    dataReceived = true;
                    break;
                }

                await WaitForDataAsync(10, cancellationToken).ConfigureAwait(false);
            }

            if (!dataReceived)
                throw new TimeoutException("No data received");

            var lastDataTime = DateTime.UtcNow;
            while (DateTime.UtcNow < totalDeadline && !cancellationToken.IsCancellationRequested)
            {
                var chunk = ReadAvailableBytes();
                if (chunk.Length > 0)
                {
                    responseStream.Write(chunk, 0, chunk.Length);
                    lastDataTime = DateTime.UtcNow;
                }
                else if ((DateTime.UtcNow - lastDataTime).TotalMilliseconds >= idleTimeout)
                {
                    break;
                }

                await WaitForDataAsync(10, cancellationToken).ConfigureAwait(false);
            }

            return responseStream.ToArray();
        }

        public byte[] Receive(int byteCount)
        {
            return ReceiveAsync(byteCount).GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            ClearReceiveBuffer();
            _tcpClient?.Stop();
            _receiveBuffer.Dispose();
        }

        public async Task<byte[]> SendAndReceiveAsync(byte[] data, int timeoutMs = 5000,
            CancellationToken cancellationToken = default)
        {
            return await SendAndReceiveAsync(data, isComplete: null, timeoutMs, cancellationToken)
                .ConfigureAwait(false);
        }

        private async Task<byte[]> SendAndReceiveAsync(
            byte[] data,
            Func<byte[], bool>? isComplete,
            int timeoutMs = 5000,
            CancellationToken cancellationToken = default)
        {
            if (_tcpClient is null || !_tcpClient.IsConnected)
                throw new InvalidOperationException("TCP client is not connected");

            try
            {
                ClearReceiveBuffer();

                if (!(_tcpClient.Send(data, data.Length)))
                    throw new InvalidOperationException("TCP send failed");

                var idleTimeout = Math.Min(200, timeoutMs / 5);
                var totalDeadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                using var responseStream = new MemoryStream();

                var dataReceived = false;
                while (DateTime.UtcNow < totalDeadline && !cancellationToken.IsCancellationRequested)
                {
                    var chunk = ReadAvailableBytes();
                    if (chunk.Length > 0)
                    {
                        responseStream.Write(chunk, 0, chunk.Length);
                        dataReceived = true;
                        break;
                    }

                    await WaitForDataAsync(10, cancellationToken).ConfigureAwait(false);
                }

                if (!dataReceived)
                    throw new TimeoutException($"No response received within {timeoutMs}ms");

                var lastDataTime = DateTime.UtcNow;
                while (DateTime.UtcNow < totalDeadline && !cancellationToken.IsCancellationRequested)
                {
                    var chunk = ReadAvailableBytes();
                    if (chunk.Length > 0)
                    {
                        responseStream.Write(chunk, 0, chunk.Length);
                        lastDataTime = DateTime.UtcNow;
                    }
                    else if ((DateTime.UtcNow - lastDataTime).TotalMilliseconds >= idleTimeout)
                    {
                        if (isComplete is not null)
                        {
                            var currentResponse = responseStream.ToArray();
                            if (!isComplete(currentResponse))
                            {
                                await WaitForDataAsync(10, cancellationToken).ConfigureAwait(false);
                                continue;
                            }
                        }

                        break;
                    }

                    await WaitForDataAsync(10, cancellationToken).ConfigureAwait(false);
                }

                if (responseStream.Length == 0)
                    throw new TimeoutException($"No response received within {timeoutMs}ms");

                return responseStream.ToArray();
            }
            catch (OperationCanceledException)
            {
                OnExecuteLog?.Invoke(this,
                    new ExecuteLogEvent
                    {
                        ClassName = nameof(TcpClient),
                        MethodName = nameof(SendAndReceiveAsync),
                        LogMessage = "Send and receive operation was cancelled"
                    });
                throw;
            }
            catch (Exception ex)
            {
                OnException?.Invoke(this,
                    new ExceptionEvent
                    {
                        ClassName = GetType().Name,
                        MethodName = nameof(SendAndReceiveAsync),
                        ErrorMessage = ex.Message
                    });
                throw;
            }
        }

        public async Task<string> SendAndReceiveStringAsync(string data, int timeoutMs = 5000,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var dataBytes = Encoding.ASCII.GetBytes(data);
                var responseBytes = await SendAndReceiveAsync(dataBytes, timeoutMs, cancellationToken)
                    .ConfigureAwait(false);
                return Encoding.ASCII.GetString(responseBytes);
            }
            catch (Exception ex)
            {
                OnException?.Invoke(this,
                    new ExceptionEvent
                    {
                        ClassName = GetType().Name,
                        MethodName = nameof(SendAndReceiveStringAsync),
                        ErrorMessage = ex.Message
                    });
                throw;
            }
        }

        public async Task<string> SendAndReceiveStringAsync(
            string data,
            Func<string, bool> isComplete,
            int timeoutMs = 5000,
            CancellationToken cancellationToken = default)
        {
            if (isComplete is null)
                throw new ArgumentNullException(nameof(isComplete));

            try
            {
                var dataBytes = Encoding.ASCII.GetBytes(data);
                var responseBytes = await SendAndReceiveAsync(
                    dataBytes,
                    response => isComplete(Encoding.ASCII.GetString(response)),
                    timeoutMs,
                    cancellationToken).ConfigureAwait(false);
                return Encoding.ASCII.GetString(responseBytes);
            }
            catch (Exception ex)
            {
                OnException?.Invoke(this,
                    new ExceptionEvent
                    {
                        ClassName = GetType().Name,
                        MethodName = nameof(SendAndReceiveStringAsync),
                        ErrorMessage = ex.Message
                    });
                throw;
            }
        }

        private void ClearReceiveBuffer()
        {
            lock (_receiveLock)
            {
                _receiveBuffer.SetLength(0);
                _receiveBuffer.Position = 0;
                _dataAvailableTcs = null;
            }
        }

        private byte[] ReadAvailableBytes(int maxCount = int.MaxValue)
        {
            lock (_receiveLock)
            {
                if (_receiveBuffer.Length == 0)
                    return Array.Empty<byte>();

                var all = _receiveBuffer.ToArray();
                var count = Math.Min(all.Length, maxCount);
                var result = new byte[count];
                Buffer.BlockCopy(all, 0, result, 0, count);

                _receiveBuffer.SetLength(0);
                _receiveBuffer.Position = 0;
                if (count < all.Length)
                    _receiveBuffer.Write(all, count, all.Length - count);

                return result;
            }
        }

        private async Task WaitForDataAsync(int timeoutMs, CancellationToken cancellationToken)
        {
            TaskCompletionSource<bool> tcs;
            lock (_receiveLock)
            {
                if (_receiveBuffer.Length > 0)
                    return;

                _dataAvailableTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                tcs = _dataAvailableTcs;
            }

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, cancellationToken))
                .ConfigureAwait(false);
            if (completed == tcs.Task)
                await tcs.Task.ConfigureAwait(false);
        }
    }
}
