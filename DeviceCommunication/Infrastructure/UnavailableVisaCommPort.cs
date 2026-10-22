using System;
using System.Threading;
using System.Threading.Tasks;
using CoreShared.Events;
using DeviceCommunication.Abstractions;

namespace DeviceCommunication.Infrastructure
{
    /// <summary>
    /// VISA 不可用时的占位通信端口，所有操作安全失败并记录原因。
    /// </summary>
    internal sealed class UnavailableVisaCommPort : ICommPort
    {
        private readonly string _reason;

        public UnavailableVisaCommPort(string reason)
        {
            _reason = string.IsNullOrWhiteSpace(reason)
                ? VisaRuntime.DefaultUnavailableMessage
                : reason;
        }

        public bool IsOpen => false;
        public Action<byte[], object>? OnDataReceived { get; set; }
        public event EventHandler<ExceptionEvent>? OnException;
        public event EventHandler<ExecuteLogEvent>? OnExecuteLog;

        public bool Initialize() => true;

        public bool Open()
        {
            RaiseUnavailable(nameof(Open));
            return false;
        }

        public void Close()
        {
        }

        public bool SendBytes(byte[] data)
        {
            RaiseUnavailable(nameof(SendBytes));
            return false;
        }

        public bool SendAsciiString(string data)
        {
            RaiseUnavailable(nameof(SendAsciiString));
            return false;
        }

        public Task SendBytesAsync(byte[] data, CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException(_reason));

        public Task<byte[]> SendAndReceiveAsync(byte[] data, int timeoutMs = 5000,
            CancellationToken cancellationToken = default) =>
            Task.FromException<byte[]>(new InvalidOperationException(_reason));

        public Task<string> SendAndReceiveStringAsync(string data, int timeoutMs = 5000,
            CancellationToken cancellationToken = default) =>
            Task.FromException<string>(new InvalidOperationException(_reason));

        public Task<string> SendAndReceiveStringAsync(string data, Func<string, bool> isComplete,
            int timeoutMs = 5000, CancellationToken cancellationToken = default) =>
            Task.FromException<string>(new InvalidOperationException(_reason));

        private void RaiseUnavailable(string methodName)
        {
            OnException?.Invoke(this, new ExceptionEvent
            {
                ClassName = nameof(UnavailableVisaCommPort),
                MethodName = methodName,
                ErrorMessage = _reason
            });
        }
    }
}
