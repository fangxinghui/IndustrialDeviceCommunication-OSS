using DeviceCommunication.Abstractions;

namespace DeviceCommunication.Models
{
    public class UsbConfiguration:IConfiguration
    {
        public string? VendorId { get; set; }
        public string? ProductId { get; set; }
        public string? SerialNumber { get; set; }
        public string? ResourceString { get; set; }
        public int TimeoutMs { get; set; } = 5000;
        public string WriteTermination { get; set; } = "\n";
        public string ReadTermination { get; set; } = "\n";
        public bool EnableReadTermination { get; set; } = true;
    }
}