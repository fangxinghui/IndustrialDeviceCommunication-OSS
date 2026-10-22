using System;
using CoreShared.Events;
using DeviceCommunication.Models;

namespace DeviceCommunication.Abstractions
{
    public interface ICanBus
    {
        /// <summary>
        /// 打开can工具
        /// </summary>
        /// <param name="deviceType"></param>
        /// <param name="deviceIndex"></param>
        /// <param name="reserved"></param>
        /// <returns></returns>
        bool OpenDevice(uint deviceType,uint deviceIndex,uint reserved);
        /// <summary>
        /// 初始化通道
        /// </summary>
        /// <param name="channel">通道索引</param>
        /// <param name="canType">can类型</param>
        /// <param name="canmode">can模式</param>
        /// <param name="abitBaud">仲裁波特率</param>
        /// <param name="dbitBaud">数据波特率</param>
        /// <param name="resistance">终端电阻使能</param>
        /// <returns></returns>
        bool InitChannel(uint channel, uint canType, uint canmode, uint abitBaud, uint dbitBaud, uint resistance);
        /// <summary>
        /// 打开通道
        /// </summary>
        /// <param name="channel">通道索引</param>
        /// <returns></returns>
        bool OpenChannel(uint channel);
        /// <summary>
        /// 重置通道
        /// </summary>
        /// <param name="channel">通道索引</param>
        /// <returns></returns>
        bool ResetChannel(uint channel);
        /// <summary>
        /// 关闭设备
        /// </summary>
        /// <returns></returns>
        bool CloseDevice();
        /// <summary>
        /// 接受CanFD数据
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <returns></returns>
        CanDataFormat ReceiveDataCanFD(uint channelIndex);
        /// <summary>
        /// 接受can数据
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <returns></returns>
        CanDataFormat ReceiveData(uint channelIndex);

        /// <summary>
        /// 发送数据 can 2.0
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <param name="data">发送的数据</param>
        /// <param name="id">id</param>
        /// <param name="frameFormat">帧格式</param>
        /// <param name="frameType">帧类型</param>
        /// <param name="frameKind">帧类型</param>
        /// <param name="len">数据长度</param>
        /// <returns></returns>
        public bool SendByte(uint channelIndex, byte[] data, uint id, FrameFormat frameFormat, FrameType frameType,
            FrameKind frameKind, int len);

        /// <summary>
        /// 发送数据 Can FD
        /// </summary>
        /// <param name="channelIndex"></param>
        /// <param name="data"></param>
        /// <param name="id"></param>
        /// <param name="frameFormat"></param>
        /// <param name="frameType"></param>
        /// <param name="frameKind"></param>
        /// <param name="len"></param>
        /// <returns></returns>
        public bool SendByteFD(uint channelIndex, byte[] data, uint id, FrameFormat frameFormat, FrameType frameType,
            FrameKind frameKind, int len);
        event EventHandler<ExceptionEvent>? OnException;
        event EventHandler<ExecuteLogEvent>? OnExecuteLog;
    }
}