namespace DeviceCommunication.Models
{
    public enum FrameFormat
    {
        标准帧,
        扩展帧,
    }

    public enum FrameType
    {
        数据帧,
        远程帧
    }
    public enum FrameKind
    {
        正常帧,
        错误帧
    }
    public enum DataDirection
    {
        Tx,
        Rx
    }
    public class CanDataFormat
    {
        public int ChannelId{get;set;}
        public ulong TimeStamp{get;set;}
        public FrameFormat FrameType{get;set;}
        public int DataLength{get;set;}
        public DataDirection  DataDirection{get;set;}
        public uint ID{get;set;}
        public byte[] Data{get;set;}
        public byte Flags{get;set;}
    }
}