namespace DeviceCommunication.Abstractions
{
    /// <summary>
    /// 共享 Modbus 总线上逻辑控制器的标识（独立于物理端口）。
    /// </summary>
    public interface IModbusSharedBusIdentity
    {
        /// <summary>逻辑设备名（如控制器配置中的 Name）</summary>
        string LogicalName { get; }

        /// <summary>Modbus 从站号</summary>
        byte SlaveId { get; }

        /// <summary>物理总线键（不含 SlaveId）</summary>
        string BusKey { get; }
    }
}
