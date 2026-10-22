using DeviceCommunication.Abstractions;

namespace DeviceCommunication.Models
{
    /// <summary>
    /// 内存 Mock PLC 的占位配置（无网络参数）。
    /// </summary>
    public sealed class MockConfiguration : IConfiguration
    {
        public string? Description { get; set; }
    }
}
