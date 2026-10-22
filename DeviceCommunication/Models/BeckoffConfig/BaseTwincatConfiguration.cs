using DeviceCommunication.Abstractions;

namespace DeviceCommunication.Models.BeckoffConfig
{
    public class BaseTwincatConfiguration:IConfiguration
    {
        public string? RemoteNetId { get; set; }
        public int Port { get; set; } = 851;
    }
}