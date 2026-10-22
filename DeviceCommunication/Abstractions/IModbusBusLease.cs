namespace DeviceCommunication.Abstractions
{
    /// <summary>
    /// 释放共享 Modbus 总线引用；最后一个使用者断开物理连接。
    /// </summary>
    public interface IModbusBusLease
    {
        void ReleaseBus();
    }
}
