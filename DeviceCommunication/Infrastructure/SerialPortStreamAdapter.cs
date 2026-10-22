using RJCP.IO.Ports;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreShared.Events;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Models;
namespace DeviceCommunication.Infrastructure
{
    internal class SerialPortStreamAdapter : ISerialPort
    {
        public event EventHandler<ExceptionEvent>? OnException;
        public event EventHandler<ExecuteLogEvent>? OnExecuteLog;
        public Action<byte[], object>? OnDataReceived { get; set; }
        private readonly SerialPortStream? _serialPort;
        public SerialPortConfiguration? Configuration { get; set; }
        private readonly bool _enableDataReceived = false;
        
        private TaskCompletionSource<byte[]>? _pendingResponseTcs;
        private readonly object _responseLock = new object();

        public SerialPortStreamAdapter(SerialPortConfiguration configuration)
        {
            Configuration = configuration;
            _serialPort = new SerialPortStream();
        }
        public bool IsOpen => _serialPort?.IsOpen ?? false;
        public bool Initialize()
        {
            try
            {
                if (_serialPort is null || Configuration is null) return false;
                _serialPort.PortName = this.Configuration.PortName;
                _serialPort.BaudRate = this.Configuration.BaudRate;
                _serialPort.DataBits = this.Configuration.DataBits;
                _serialPort.StopBits = this.Configuration.StopBits switch
                {
                    SerialParameters.SerialStopBits.One => StopBits.One,
                    SerialParameters.SerialStopBits.OnePointFive => StopBits.One5,
                    SerialParameters.SerialStopBits.Two => StopBits.Two,
                    _ => StopBits.One
                };
                _serialPort.Parity = this.Configuration.Parity switch
                {
                    SerialParameters.SerialParity.None => Parity.None,
                    SerialParameters.SerialParity.Odd => Parity.Odd,
                    SerialParameters.SerialParity.Mark => Parity.Mark,
                    SerialParameters.SerialParity.Space => Parity.Space,
                    SerialParameters.SerialParity.Even => Parity.Even,
                    _=>Parity.None
                };
                OnExecuteLog?.Invoke(this,new ExecuteLogEvent()
                {
                    ClassName = nameof(SerialPortStreamAdapter),
                    MethodName = nameof(Initialize),
                    LogMessage = $"{_serialPort.PortName} Initializing"
                });
                _serialPort.DataReceived -= OnDataReceive;
                //_serialPort.DataReceived += OnDataReceived;
                return true;
            }
            catch(Exception ex)
            {
                OnException?.Invoke(this, new ExceptionEvent
                {
                    ClassName = GetType().Name,
                    MethodName = nameof(Initialize),
                    ErrorMessage = ex.Message
                });
                return false;
            }
        }

        private void OnDataReceive(object sender, SerialDataReceivedEventArgs e)
        {
            if (_serialPort is null) return;
            byte[] buffer = new byte[_serialPort.BytesToRead];
            int count=_serialPort.Read(buffer,0, buffer.Length);
            
            // 如果有等待的响应请求，先完成它
            lock (_responseLock)
            {
                if (_pendingResponseTcs != null && !_pendingResponseTcs.Task.IsCompleted)
                {
                    byte[] actualData = new byte[count];
                    Array.Copy(buffer, actualData, count);
                    _pendingResponseTcs.TrySetResult(actualData);
                    _pendingResponseTcs = null;
                    return;
                }
            }
            
            // 否则触发普通的数据接收事件
            OnDataReceived?.Invoke(buffer,this);
        }

        public void Close()
        {
            try
            {
                if (_serialPort is null) return;
                
                // 取消任何等待的响应
                lock (_responseLock)
                {
                    _pendingResponseTcs?.TrySetCanceled();
                    _pendingResponseTcs = null;
                }
                
                _serialPort.DataReceived -= OnDataReceive;
                _serialPort.Close();
            }
            catch(Exception ex)
            {
                OnException?.Invoke(this, new ExceptionEvent
                {
                    ClassName = GetType().Name,
                    MethodName = nameof(Close),
                    ErrorMessage = ex.Message
                });
            }
        }

        public bool Open()
        {
            try
            {
                if(_serialPort is null) return false;
                if (_serialPort.IsOpen) _serialPort.Close();
                _serialPort.Open();

                if (_enableDataReceived)
                {
                    _serialPort.DataReceived -= OnDataReceive;
                    _serialPort.DataReceived += OnDataReceive;
                }

                return true;
            }
            catch(Exception ex)
            {
                OnException?.Invoke(this, new ExceptionEvent
                {
                    ClassName = GetType().Name,
                    MethodName = nameof(Open),
                    ErrorMessage = ex.Message
                });
                return false;
            }
        }
        public bool SendAsciiString(string data)
        {
            try
            {
                byte[]buff=Encoding.ASCII.GetBytes(data);
                _serialPort?.Write(buff, 0, buff.Length);
                return true;
            }
            catch (Exception ex)
            {
                OnException?.Invoke(this, new ExceptionEvent
                {
                    ClassName = GetType().Name,
                    MethodName = nameof(SendAsciiString),
                    ErrorMessage = ex.Message
                });
                return false;
            }
        }

        public bool SendBytes(byte[] data)
        {
            try
            {
                _serialPort?.Write(data, 0, data.Length);
                return true;
            }
            catch (Exception ex)
            {
                OnException?.Invoke(this, new ExceptionEvent
                {
                    ClassName = GetType().Name,
                    MethodName = nameof(SendBytes),
                    ErrorMessage = ex.Message
                });
                return false;
            }
        }

        public async Task SendBytesAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_serialPort is null)
                {
                    OnException?.Invoke(this, new ExceptionEvent
                    {
                        ClassName = GetType().Name,
                        MethodName = nameof(SendBytesAsync),
                        ErrorMessage = "Serial port is not initialized"
                    });
                    return;
                }

                await _serialPort.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                OnExecuteLog?.Invoke(this, new ExecuteLogEvent()
                {
                    ClassName = nameof(SerialPortStreamAdapter),
                    MethodName = nameof(SendBytesAsync),
                    LogMessage = "Send operation was cancelled"
                });
            }
            catch (Exception ex)
            {
                OnException?.Invoke(this, new ExceptionEvent
                {
                    ClassName = GetType().Name,
                    MethodName = nameof(SendBytesAsync),
                    ErrorMessage = ex.Message
                });
            }
        }

        public async Task<byte[]> SendAndReceiveAsync(byte[] data, int timeoutMs = 5000, CancellationToken cancellationToken = default)
        {
            return await SendAndReceiveAsync(data, isComplete: null, timeoutMs, cancellationToken).ConfigureAwait(false);
        }

        private async Task<byte[]> SendAndReceiveAsync(
            byte[] data,
            Func<byte[], bool>? isComplete,
            int timeoutMs = 5000,
            CancellationToken cancellationToken = default)
        {
            if (_serialPort is null)
            {
                throw new InvalidOperationException("Serial port is not initialized");
            }

            try
            {
                // 发送前丢弃接收缓冲区中的旧数据
                _serialPort.DiscardInBuffer();

                // 发送数据
                await _serialPort.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);

                // 使用轮询方式读取响应，解决 DataReceived 事件只能触发一次导致数据不完整的问题
                var idleTimeout = Math.Min(200, timeoutMs / 5); // 静默超时：200ms内无新数据则认为响应完成
                var totalDeadline = DateTime.Now.AddMilliseconds(timeoutMs);

                using var responseStream = new System.IO.MemoryStream();
                var buffer = new byte[4096];

                // 先等待至少有一些数据到达
                bool dataReceived = false;
                while (DateTime.Now < totalDeadline && !cancellationToken.IsCancellationRequested)
                {
                    if (_serialPort.BytesToRead > 0)
                    {
                        dataReceived = true;
                        break;
                    }
                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                }

                if (!dataReceived)
                {
                    throw new TimeoutException($"No response received within {timeoutMs}ms");
                }

                // 持续读取直到静默期内没有新数据
                var lastDataTime = DateTime.Now;
                while (DateTime.Now < totalDeadline && !cancellationToken.IsCancellationRequested)
                {
                    int bytesAvailable = _serialPort.BytesToRead;
                    if (bytesAvailable > 0)
                    {
                        int bytesRead = _serialPort.Read(buffer, 0, Math.Min(bytesAvailable, buffer.Length));
                        responseStream.Write(buffer, 0, bytesRead);
                        lastDataTime = DateTime.Now;
                    }
                    else if ((DateTime.Now - lastDataTime).TotalMilliseconds >= idleTimeout)
                    {
                        if (isComplete is not null)
                        {
                            var currentResponse = responseStream.ToArray();
                            if (!isComplete(currentResponse))
                            {
                                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                                continue;
                            }
                        }

                        // 静默期结束，响应读取完成
                        break;
                    }

                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                }

                if (responseStream.Length == 0)
                {
                    throw new TimeoutException($"No response received within {timeoutMs}ms");
                }

                return responseStream.ToArray();
            }
            catch (OperationCanceledException)
            {
                OnExecuteLog?.Invoke(this, new ExecuteLogEvent
                {
                    ClassName = nameof(SerialPortStreamAdapter),
                    MethodName = nameof(SendAndReceiveAsync),
                    LogMessage = "Send and receive operation was cancelled"
                });
                throw;
            }
            catch (Exception ex)
            {
                OnException?.Invoke(this, new ExceptionEvent
                {
                    ClassName = GetType().Name,
                    MethodName = nameof(SendAndReceiveAsync),
                    ErrorMessage = ex.Message
                });
                throw;
            }
        }

        public async Task<byte[]> ReceiveForDurationAsync(int durationMs, CancellationToken cancellationToken = default)
        {
            if (_serialPort is null)
                throw new InvalidOperationException("Serial port is not initialized");
            if (!_serialPort.IsOpen)
                throw new InvalidOperationException("Serial port is not open");
            if (durationMs <= 0)
                return Array.Empty<byte>();

            using var responseStream = new System.IO.MemoryStream();
            var buffer = new byte[4096];
            var deadline = DateTime.UtcNow.AddMilliseconds(durationMs);

            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                int available = _serialPort.BytesToRead;
                if (available > 0)
                {
                    int bytesRead = _serialPort.Read(buffer, 0, Math.Min(available, buffer.Length));
                    if (bytesRead > 0)
                        responseStream.Write(buffer, 0, bytesRead);
                }
                else
                {
                    await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return responseStream.ToArray();
        }

        public async Task<string> SendAndReceiveStringAsync(string data, int timeoutMs = 5000, CancellationToken cancellationToken = default)
        {
            try
            {
                byte[] dataBytes = Encoding.ASCII.GetBytes(data);
                byte[] responseBytes = await SendAndReceiveAsync(dataBytes, timeoutMs, cancellationToken).ConfigureAwait(false);
                return Encoding.ASCII.GetString(responseBytes);
            }
            catch (Exception ex)
            {
                OnException?.Invoke(this, new ExceptionEvent
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
            {
                throw new ArgumentNullException(nameof(isComplete));
            }

            try
            {
                byte[] dataBytes = Encoding.ASCII.GetBytes(data);
                byte[] responseBytes = await SendAndReceiveAsync(
                    dataBytes,
                    response => isComplete(Encoding.ASCII.GetString(response)),
                    timeoutMs,
                    cancellationToken).ConfigureAwait(false);
                return Encoding.ASCII.GetString(responseBytes);
            }
            catch (Exception ex)
            {
                OnException?.Invoke(this, new ExceptionEvent
                {
                    ClassName = GetType().Name,
                    MethodName = nameof(SendAndReceiveStringAsync),
                    ErrorMessage = ex.Message
                });
                throw;
            }
        }
    }
}
