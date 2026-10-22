using DeviceCommunication.Abstractions;

namespace DeviceCommunication.Models
{
    public class SiemensNetConfiguration : IConfiguration
    {
        public string? IP { get; set; }
        public int Port { get; set; }
        public byte Rack { get; set; }
        public byte Slot { get; set; }
        public CoreShared.SiemensPLCType PLCType { get; set; }
    }
}
