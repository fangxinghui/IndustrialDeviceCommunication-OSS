using CoreShared;

namespace DeviceCommunication.Protocols.Mock
{
    /// <summary>
    /// Mock 控制器单点写入通知（供上层运动/厂内规则订阅）。
    /// </summary>
    public sealed class MockAddressWrittenEventArgs : System.EventArgs
    {
        public MockAddressWrittenEventArgs(ControllerAddress address, object? value, string key)
        {
            Address = address;
            Value = value;
            Key = key;
        }

        public ControllerAddress Address { get; }
        public object? Value { get; }
        /// <summary>字典键（Address 或 Name）。</summary>
        public string Key { get; }
    }
}
