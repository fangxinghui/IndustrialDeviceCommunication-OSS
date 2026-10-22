using DeviceCommunication.Abstractions;

namespace DeviceCommunication.Models
{
    public class ModbusRtuConfiguration:SerialPortConfiguration,IModbus
    {
        public bool IsAddressStartWithZero { get; set; }
        public byte StationId { get; set; }
        public ModbusDataType ModbusDataType { get; set; }
    }
}