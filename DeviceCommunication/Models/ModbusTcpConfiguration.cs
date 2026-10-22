using DeviceCommunication.Abstractions;

namespace DeviceCommunication.Models
{
    public class ModbusTcpConfiguration:NetConfiguration,IModbus
    {
        public bool IsAddressStartWithZero { get; set; }
        public byte StationId { get; set; }
        public ModbusDataType ModbusDataType { get; set; }
    }
}