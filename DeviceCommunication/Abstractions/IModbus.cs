namespace DeviceCommunication.Abstractions
{
    public interface IModbus
    {
        public bool IsAddressStartWithZero { get; set; }
        public byte StationId { get; set;}
        public ModbusDataType ModbusDataType { get; set; }
    }
}