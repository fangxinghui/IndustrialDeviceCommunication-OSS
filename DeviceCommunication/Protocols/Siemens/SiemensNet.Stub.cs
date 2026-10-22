using System;
using CoreShared;
using CoreShared.Events;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Models;

namespace DeviceCommunication.Protocols.Siemens
{
    /// <summary>
    /// Stand-in for <c>SiemensNet</c> when HslCommunication is not referenced.
    /// Rebuild with <c>-p:IncludeHslDrivers=true -p:HslCommunicationDll=...</c>
    /// to compile Protocols/Siemens/SiemensNet.cs instead of this file.
    /// </summary>
    internal class SiemensNet : IController
    {
        private const string DisabledMessage =
            "Siemens S7 support is not compiled in. Install HslCommunication and rebuild with -p:IncludeHslDrivers=true -p:HslCommunicationDll=<path to HslCommunication.dll>.";

        public bool IsConnected => false;

        public event EventHandler<ExceptionEvent>? OnException;

        public event EventHandler<ExecuteLogEvent>? OnExecuteLog;

        public IConfiguration? CreateConfiguration() => null;

        public bool Initialize<T>(T parameter)
        {
            OnExecuteLog?.Invoke(this, new ExecuteLogEvent
            {
                LogMessage = DisabledMessage,
                ClassName = nameof(SiemensNet),
                MethodName = nameof(Initialize)
            });
            return false;
        }

        public bool Connect(int timeout)
        {
            OnException?.Invoke(this, new ExceptionEvent
            {
                ErrorMessage = DisabledMessage,
                ClassName = nameof(SiemensNet),
                MethodName = nameof(Connect)
            });
            return false;
        }

        public void Disconnect()
        {
        }

        public (bool success, object value) ReadValue(ControllerAddress? address)
            => (false, null!);

        public bool WriteValue<T>(ControllerAddress? address, T value) => false;

        public (bool success, object[] values) ReadValues<T>(ControllerAddress[]? addresses, ushort length)
            => (false, Array.Empty<object>());

        public bool WriteValues<T>(ControllerAddress[]? addresses, T[]? values) => false;
    }
}
