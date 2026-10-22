using CoreShared;
using CoreShared.Interfaces;

namespace DeviceCommunication.Abstractions
{
    public interface IController : ILogEvent
    {
        bool IsConnected { get; }

        /// <summary>
        /// 由控制器根据构造时传入的元数据，生成设备 IConfiguration。
        /// </summary>
        IConfiguration? CreateConfiguration();

        bool Initialize<T>(T parameter);
        bool Connect(int timeout);
        void Disconnect();
        (bool success, object value) ReadValue(ControllerAddress? address);
        bool WriteValue<T>(ControllerAddress? address, T value);
        (bool success, object[] values) ReadValues<T>(ControllerAddress[]? addresses, ushort length);
        bool WriteValues<T>(ControllerAddress[]? addresses, T[]? values);
    }
}
