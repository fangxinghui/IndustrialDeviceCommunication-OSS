namespace CoreShared
{
    public enum CommonType
    {
        GPIB,
        USB,
        LAN,
        Serial,
        /// <summary>通过厂商 DLL / 本地软件调用的仪表（如 FlexSystem、TrustSystem）。</summary>
        DLL
    }
}