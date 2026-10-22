namespace DeviceCommunication.Models
{
    public class ControllerConfiguration
    {
        /// <summary>
        /// 原始品牌名称，来自控制器配置/目录，用于反射驱动匹配。
        /// </summary>
        public string? BrandName { get; set; }
        public string? Model{get;set;}
        public int UpdateInterval { get; set; } = 100;
        public string? Description { get; set; }
        public string? Parameters { get; set; }
        public ConnectionType ConnectionType { get; set; } = ConnectionType.Network;
        public ControllerType ControllerType { get; set; } = ControllerType.PLC;
    }
}