using System;
using DeviceCommunication.Abstractions;

namespace DeviceCommunication.Protocols
{
    /// <summary>将共享总线能力从内部协议控制器转发到外层驱动。</summary>
    internal static class ModbusBusControllerFacade
    {
        public static IDisposable EnterPollScope(IController? controller)
            => controller is IModbusBusPollScope scope ? scope.EnterPollScope() : NullPollScope.Instance;

        public static string GetLogicalName(IController? controller, string? fallback = null)
            => controller is IModbusSharedBusIdentity identity && !string.IsNullOrWhiteSpace(identity.LogicalName)
                ? identity.LogicalName
                : fallback ?? string.Empty;

        public static byte GetSlaveId(IController? controller) =>
            controller is IModbusSharedBusIdentity identity ? identity.SlaveId : (byte)0;

        public static string GetBusKey(IController? controller) =>
            controller is IModbusSharedBusIdentity identity ? identity.BusKey : string.Empty;
    }
}
