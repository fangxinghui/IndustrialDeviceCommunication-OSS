using DeviceCommunication.Abstractions;

namespace DeviceCommunication.Models
{
    public class GpibConfiguration:IConfiguration
    {
        public string? BoardIndex{ get; set; }
        public string? PrimaryAddress{ get; set; }
    }
}