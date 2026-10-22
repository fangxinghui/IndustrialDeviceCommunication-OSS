namespace DeviceCommunication.Models.BeckoffConfig
{
    public class BaseConfiguration:BaseTwincatConfiguration
    {
        /// <summary>
        /// PLC的IP
        /// </summary>
        public string? RemoteAddress { get; set;}
        /// <summary>
        /// PC的IP
        /// </summary>
        public string? LocalAddress { get; set;}
        /// <summary>
        /// PC的网络ID
        /// </summary>
        public string? LocalNetId { get; set;}
        /// <summary>
        /// 路由名称
        /// </summary>
        public string? RouterName { get; set; }
    }
}