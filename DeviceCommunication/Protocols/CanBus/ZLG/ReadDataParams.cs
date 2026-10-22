using System;

namespace DeviceCommunication.Protocols.CanBus.ZLG
{
    internal sealed class ReadDataParams
    {
        /// <summary>
        /// 设备句柄
        /// </summary>
        internal IntPtr DevHandler{get;set;}
        /// <summary>
        /// 通道句柄
        /// </summary>
        internal IntPtr ChannelHandler{get;set;}
        /// <summary>
        /// 通道号
        /// </summary>
        internal int Channel{get;set;}
    }
}