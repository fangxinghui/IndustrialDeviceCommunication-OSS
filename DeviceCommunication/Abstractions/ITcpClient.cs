namespace DeviceCommunication.Abstractions
{
    public interface ITcpClient:ITcp
    {
        public bool IsConnected { get; }
    }
}