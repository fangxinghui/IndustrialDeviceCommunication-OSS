namespace DeviceCommunication.Models
{
    public class UsbVisaDeviceInfo
    {
        public string ResourceString { get; set; } = string.Empty;
        public string? VendorId { get; set; }
        public string? ProductId { get; set; }
        public string? SerialNumber { get; set; }

        public string DisplayName => string.IsNullOrWhiteSpace(ResourceString)
            ? "USB VISA device"
            : ResourceString;

        public override string ToString() => DisplayName;
    }
}
