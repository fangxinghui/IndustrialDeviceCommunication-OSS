using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using CoreShared.Events;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Models;

namespace DeviceCommunication.Protocols.CanBus.ZLG
{
    internal class CanBusZlg:ICanBus
    {
        public event EventHandler<ExceptionEvent>? OnException;
        public event EventHandler<ExecuteLogEvent>? OnExecuteLog;
        private IntPtr _deviceHandler=IntPtr.Zero;
        private readonly Dictionary<uint,IntPtr>_channels=new Dictionary<uint,IntPtr>();
        private const int BufferSize = 100;  //缓存区大小
        private static string GetZlgDeviceTypeName(uint deviceType)
        {
            var fields = typeof(ZLGCAN).GetFields(BindingFlags.Public | BindingFlags.Static);

            var match = fields.FirstOrDefault(f =>
                (f.FieldType == typeof(uint) || f.FieldType == typeof(UInt32)) &&
                (uint)f.GetValue(null)! == deviceType);

            return match?.Name ?? $"UNKNOWN({deviceType})";
        }
        /// <summary>
        /// 打开can工具
        /// </summary>
        /// <param name="deviceType"></param>
        /// <param name="deviceIndex"></param>
        /// <param name="reserved"></param>
        /// <returns></returns>
        public bool OpenDevice(uint deviceType, uint deviceIndex, uint reserved)
        {
            try
            {
                _deviceHandler=ZLGCAN.ZCAN_OpenDevice(deviceType, deviceIndex, reserved);
                var typeName = GetZlgDeviceTypeName(deviceType);
                if (_deviceHandler != IntPtr.Zero)
                {
                    //写log 打开设备成功
                    OnExecuteLog?.Invoke(this, new ExecuteLogEvent
                    {
                        ClassName = nameof(CanBusZlg),
                        MethodName = nameof(OpenDevice),
                        LogMessage = $"OpenDevice success. deviceType={deviceType} ({typeName}), deviceIndex={deviceIndex}"
                    });
                    return true;
                }
                else
                {
                    OnExecuteLog?.Invoke(this,new ExecuteLogEvent()
                    {
                        ClassName =  nameof(CanBusZlg),
                        MethodName = nameof(OpenDevice),
                        LogMessage = $"OpenDevice failed. deviceType={deviceType} ({typeName}),deviceIndex={deviceIndex}"
                    });
                    return false;
                }
            }
            catch (Exception e)
            {
                OnException?.Invoke(this, new ExceptionEvent(){ErrorMessage =  e.Message,ClassName = nameof(CanBusZlg),MethodName =  nameof(OpenDevice)});
                return false;
            }
        }
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
        public bool InitChannel(uint channel,uint canType,uint canmode,uint abitBaud,uint dbitBaud,uint resistance)
        {
            IntPtr temp = IntPtr.Zero;
            OnExecuteLog?.Invoke(this,new ExecuteLogEvent(){ClassName = nameof(CanBusZlg),MethodName = nameof(InitChannel),LogMessage = $"InitChannel:{channel}"});
            try
            {
                string path = String.Format("{0}/canfd_abit_baud_rate", channel);
                OnExecuteLog?.Invoke(this,new  ExecuteLogEvent(){ClassName = nameof(CanBusZlg),MethodName = nameof(InitChannel),LogMessage = $"InitChannel 设置仲裁波特率{abitBaud}"});
                uint ret = ZLGCAN.ZCAN_SetValue(_deviceHandler, path, abitBaud.ToString());
                if (ret != 1)
                {
                    OnExecuteLog?.Invoke(this,
                        new ExecuteLogEvent() { ClassName = nameof(CanBusZlg), MethodName = nameof(InitChannel),LogMessage =$"InitChannel 设置仲裁波特率{abitBaud} failed." });
                    return false;
                }
                OnExecuteLog?.Invoke(this,new  ExecuteLogEvent(){ClassName = nameof(CanBusZlg),MethodName = nameof(InitChannel),LogMessage = $"InitChannel 设置数据域波特率{dbitBaud}"});
                path = String.Format("{0}/canfd_dbit_baud_rate", channel);
                ret = ZLGCAN.ZCAN_SetValue(_deviceHandler, path, dbitBaud.ToString());
                if (ret != 1)
                {
                    OnExecuteLog?.Invoke(this,
                        new ExecuteLogEvent() { ClassName = nameof(CanBusZlg), MethodName = nameof(InitChannel),LogMessage =$"InitChannel 设置数据域波特率{dbitBaud} failed." });
                    return false;
                }
                OnExecuteLog?.Invoke(this,new  ExecuteLogEvent(){ClassName = nameof(CanBusZlg),MethodName = nameof(InitChannel),LogMessage = $"InitChannel 设置终端电阻使能{resistance}"});
                path = String.Format("{0}/initenal_resistance", channel);
                ret = ZLGCAN.ZCAN_SetValue(_deviceHandler, path, resistance.ToString());
                if (ret != 1)
                {
                    OnExecuteLog?.Invoke(this,new  ExecuteLogEvent(){ClassName = nameof(CanBusZlg),MethodName = nameof(InitChannel),LogMessage = $"InitChannel 设置终端电阻使能{resistance} failed." });
                    return false;
                }
                ZLGCAN.ZCAN_CHANNEL_INIT_CONFIG initConfig = new ZLGCAN.ZCAN_CHANNEL_INIT_CONFIG();
                initConfig.can_type = canType;
                initConfig.config.canfd.mode = (byte)canmode;
                IntPtr P2InitConfig=Marshal.AllocHGlobal(Marshal.SizeOf(initConfig));
                Marshal.StructureToPtr(initConfig, P2InitConfig, true);
                temp = ZLGCAN.ZCAN_InitCAN(_deviceHandler, channel, P2InitConfig);
                Marshal.FreeHGlobal(P2InitConfig);
                if (temp != IntPtr.Zero)
                {
                    _channels[channel] = temp;
                    OnExecuteLog?.Invoke(this,new ExecuteLogEvent(){ClassName = nameof(CanBusZlg),MethodName = nameof(InitChannel),LogMessage = $"InitChannel success. channel={channel}"});
                    return true;
                }
                OnExecuteLog?.Invoke(this,new ExecuteLogEvent(){ClassName = nameof(CanBusZlg),MethodName = nameof(InitChannel),LogMessage = $"InitChannel failed. channel={channel}"});
                return false;
            }
            catch (Exception e)
            {
                OnException?.Invoke(this, new ExceptionEvent(){ClassName = nameof(CanBusZlg),MethodName =  nameof(InitChannel),ErrorMessage =$"Initial channel:{channel} failed.{e.Message}"});
                return false;
            }
        }
        /// <summary>
        /// 打开通道
        /// </summary>
        /// <param name="channel">通道索引</param>
        /// <returns></returns>
        public bool OpenChannel(uint channel)
        {
            try
            {
                if (!_channels.TryGetValue(channel, out IntPtr temp))
                {
                    //不存在
                    OnExecuteLog?.Invoke(this,new ExecuteLogEvent(){ClassName = nameof(CanBusZlg),MethodName = nameof(OpenChannel),LogMessage = $"openchannel:{channel} failed,channel is not initial"});
                    return false;   
                }
                uint ret=ZLGCAN.ZCAN_StartCAN(temp);
                if (ret != 1)
                {
                    OnExecuteLog?.Invoke(this,new ExecuteLogEvent(){ClassName = nameof(CanBusZlg),MethodName = nameof(OpenChannel),LogMessage = $"openchannel:{channel} failed."});
                    return false;   
                }
                OnExecuteLog?.Invoke(this,new ExecuteLogEvent(){ClassName = nameof(CanBusZlg),MethodName =  nameof(OpenChannel),LogMessage = $"openchannel:{channel} success"});
                return true;
            }
            catch (Exception e)
            {
                OnException?.Invoke(this,new ExceptionEvent(){ClassName = nameof(CanBusZlg),MethodName = nameof(OpenChannel),ErrorMessage =$"OpenChannel fail:{e.Message}"});
                return false;
            }
        }
        /// <summary>
        /// 重置通道
        /// </summary>
        /// <param name="channel">通道索引</param>
        /// <returns></returns>
        public bool ResetChannel(uint channel)
        {
            try
            {
                if (!_channels.TryGetValue(channel, out IntPtr temp))
                {
                    //不存在
                    OnExecuteLog?.Invoke(this,new ExecuteLogEvent(){ClassName = nameof(CanBusZlg),MethodName = nameof(ResetChannel),LogMessage = $"ResetChannel:{channel} failed,channel is not initial"});
                    return false;   
                }
                uint ret=ZLGCAN.ZCAN_ResetCAN(temp);
                if (ret != 1)
                {
                    OnExecuteLog?.Invoke(this,new ExecuteLogEvent(){ClassName = nameof(CanBusZlg),MethodName = nameof(ResetChannel),LogMessage = $"ResetChannel:{channel} failed."});
                    return false;   
                }
                OnExecuteLog?.Invoke(this,new ExecuteLogEvent(){ClassName = nameof(CanBusZlg),MethodName =  nameof(ResetChannel),LogMessage = $"ResetChannel:{channel} success"});
                return true;
            }
            catch (Exception e)
            {
                OnException?.Invoke(this,new ExceptionEvent(){ClassName = nameof(CanBusZlg),MethodName = nameof(ResetChannel),ErrorMessage = $"ResetChannel fail:{e.Message}"});
                return false;
            }
        }
        /// <summary>
        /// 关闭设备
        /// </summary>
        /// <returns></returns>
        public bool CloseDevice()
        {
            try
            {
                if (ZLGCAN.ZCAN_CloseDevice(_deviceHandler) != 1)
                {
                    OnExecuteLog?.Invoke(this,new ExecuteLogEvent(){ClassName = nameof(CanBusZlg),MethodName =  nameof(CloseDevice),LogMessage = $"CloseDevice:{_deviceHandler} failed."});
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                OnException?.Invoke(this,new ExceptionEvent(){ClassName=nameof(CanBusZlg),MethodName = nameof(CloseDevice),ErrorMessage=$"CloseDevice fail:{e.Message}"});
                return false;
            }
        }
        /// <summary>
        /// 接受CanFD数据
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <returns></returns>
        public CanDataFormat ReceiveDataCanFD(uint channelIndex)
        {
            CanDataFormat canDataFormat = new CanDataFormat();
            if(!_channels.TryGetValue(channelIndex, out IntPtr temp))
                return canDataFormat;
            if (_deviceHandler == IntPtr.Zero)
            {
                return canDataFormat;
            }
            ReadDataParams param = new ReadDataParams();
            // 预计算结构体大小
            int canStructSize = Marshal.SizeOf(typeof(ZLGCAN.ZCAN_Receive_Data));
            int canfdStructSize = Marshal.SizeOf(typeof(ZLGCAN.ZCAN_ReceiveFD_Data));
            // 分配非托管内存（循环外一次分配，复用避免频繁申请释放）
            IntPtr pCanBuffer = Marshal.AllocHGlobal(canStructSize * BufferSize);      // CAN接收缓冲区
            IntPtr pCanfdBuffer = Marshal.AllocHGlobal(canfdStructSize * BufferSize);
            try
            {
                uint recNum = ZLGCAN.ZCAN_GetReceiveNum(param.ChannelHandler, 1); // 1-CANFD
                if (recNum > 0)
                {
                    uint actualReceive = ZLGCAN.ZCAN_ReceiveFD(
                        param.ChannelHandler,
                        pCanfdBuffer, // CANFD非托管缓冲区指针
                        BufferSize,
                        10
                    );
                    if (actualReceive > 0)
                    {
                        for (int i = 0; i < actualReceive; i++)
                        {
                            // 计算当前CANFD数据的指针
                            IntPtr pCurrentCanfd = (IntPtr)(pCanfdBuffer.ToInt64() + i * canfdStructSize);
                            // 非托管内存转换为C#结构体
                            ZLGCAN.ZCAN_ReceiveFD_Data recFDData = (ZLGCAN.ZCAN_ReceiveFD_Data)Marshal.PtrToStructure(
                                pCurrentCanfd,
                                typeof(ZLGCAN.ZCAN_ReceiveFD_Data)
                            );
                            canDataFormat = new CanDataFormat()
                            {
                                ChannelId = param.Channel,
                                TimeStamp = recFDData.timestamp,
                                FrameType = (recFDData.frame.can_id & (1 << 31)) != 0?FrameFormat.扩展帧:FrameFormat.标准帧,
                                DataDirection = (recFDData.frame.flags & 0x20) == 0x20  ?DataDirection.Tx:DataDirection.Rx,
                                DataLength =Math.Min(recFDData.frame.len, recFDData.frame.data != null ? recFDData.frame.data.Length : 0),
                                ID = recFDData.frame.can_id & 0x1FFFFFFF,
                                Data = recFDData.frame.data!,
                                Flags =recFDData.frame.flags
                                
                            };
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

            return canDataFormat;
        }
        /// <summary>
        /// 接受can数据
        /// </summary>
        /// <param name="channelIndex">通道索引</param>
        /// <returns></returns>
        public CanDataFormat ReceiveData(uint channelIndex)
        {
            CanDataFormat canDataFormat = new CanDataFormat();
            if(!_channels.TryGetValue(channelIndex, out IntPtr temp))
                return canDataFormat;
            if (_deviceHandler == IntPtr.Zero)
            {
                return canDataFormat;
            }
            ReadDataParams param = new ReadDataParams();
            // 预计算结构体大小
            int canStructSize = Marshal.SizeOf(typeof(ZLGCAN.ZCAN_Receive_Data));
            int canfdStructSize = Marshal.SizeOf(typeof(ZLGCAN.ZCAN_ReceiveFD_Data));
            // 分配非托管内存（循环外一次分配，复用避免频繁申请释放）
            IntPtr pCanBuffer = Marshal.AllocHGlobal(canStructSize * BufferSize);      // CAN接收缓冲区
            IntPtr pCanfdBuffer = Marshal.AllocHGlobal(canfdStructSize * BufferSize);
            try
            {
                uint recNum = ZLGCAN.ZCAN_GetReceiveNum(param.ChannelHandler, 0);
                if (recNum > 0)
                {
                    uint actualReceive = ZLGCAN.ZCAN_Receive(
                        param.ChannelHandler,
                        pCanBuffer,    // 传入非托管缓冲区指针（C风格）
                        BufferSize,
                        10
                    );
                    if (actualReceive > 0)
                    {
                        for (int i = 0; i < actualReceive; i++)
                        {
                            // 计算当前CAN数据的指针
                            IntPtr pCurrentCan = (IntPtr)(pCanBuffer.ToInt64() + i * canStructSize);

                            // 非托管内存转换为C#结构体
                            ZLGCAN.ZCAN_Receive_Data recData = (ZLGCAN.ZCAN_Receive_Data)Marshal.PtrToStructure(
                                pCurrentCan,
                                typeof(ZLGCAN.ZCAN_Receive_Data)
                            );
                            canDataFormat = new CanDataFormat()
                            {
                                ChannelId = param.Channel,
                                TimeStamp = recData.timestamp,
                                FrameType = (recData.frame.can_id & (1 << 31)) != 0?FrameFormat.扩展帧:FrameFormat.标准帧,
                                DataDirection = (recData.frame.__pad & 0x20) == 0x20 ?DataDirection.Tx:DataDirection.Rx,
                                DataLength = Math.Min(recData.frame.can_dlc, recData.frame.data != null ? recData.frame.data.Length : 0),
                                ID = recData.frame.can_id & 0x1FFFFFFF,
                                Data = recData.frame.data!
                            };
                        }
                    }
                }
            }
            catch (Exception e)
            {
                OnException?.Invoke(this,new ExceptionEvent(){ClassName = nameof(CanBusZlg),MethodName = nameof(ReceiveData),ErrorMessage = e.Message});
            }
            return canDataFormat;
        }
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
        public bool SendByte(uint channelIndex, byte[] data,uint id,FrameFormat frameFormat,FrameType frameType,FrameKind frameKind,int len)
        {
            try
            {
                if (!_channels.TryGetValue(channelIndex, out IntPtr channelhandler))
                    return false;
                if (channelhandler == IntPtr.Zero)
                    return false;
                ZLGCAN.ZCAN_Transmit_Data can_data = new ZLGCAN.ZCAN_Transmit_Data();
                IntPtr p2CanData = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ZLGCAN.ZCAN_Transmit_Data)) * 1);
                can_data.frame = new ZLGCAN.can_frame();
                can_data.transmit_type = 0;
                can_data.frame.can_id = MakeCanId(id, (int)frameFormat, (int)frameType, (int)frameKind);
                can_data.frame.can_dlc = (byte)len;
                can_data.frame.__pad = 0x20;
                can_data.frame.data = data;
                Array.Copy(data, can_data.frame.data, Math.Min(8, data.Length));
                Marshal.StructureToPtr(can_data,
                    (IntPtr)(p2CanData + Marshal.SizeOf(typeof(ZLGCAN.ZCAN_Transmit_Data))), true);

                uint ret = ZLGCAN.ZCAN_Transmit(channelhandler, p2CanData, 1);
                OnExecuteLog?.Invoke(this,
                    new ExecuteLogEvent()
                        { ClassName = nameof(CanBusZlg), MethodName = nameof(SendByte), LogMessage = $"发送数据结果：{ret}" });
                Marshal.FreeHGlobal(p2CanData);
                return ret == 1;
            }
            catch (Exception e)
            {
                OnException?.Invoke(this,
                    new ExceptionEvent()
                        { ClassName = nameof(CanBusZlg), MethodName = nameof(SendByte), ErrorMessage = e.Message });
                return false;
            }
            finally
            {
                
            }
        }
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
        public bool SendByteFD(uint channelIndex, byte[] data,uint id,FrameFormat frameFormat,FrameType frameType,FrameKind frameKind,int len)
        {
            try
            {
                if (!_channels.TryGetValue(channelIndex, out IntPtr channelhandler))
                    return false;
                if (channelhandler == IntPtr.Zero)
                    return false;
                ZLGCAN.ZCAN_TransmitFD_Data canfd_data = new ZLGCAN.ZCAN_TransmitFD_Data();
                IntPtr p2CanFDData = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ZLGCAN.ZCAN_TransmitFD_Data)) );

                canfd_data.frame = new ZLGCAN.canfd_frame();
                canfd_data.transmit_type = 0;            // 0-正常发送
                canfd_data.frame.can_id = MakeCanId(id, (int)frameFormat, (int)frameType, (int)frameKind);
                canfd_data.frame.len = (byte)len;               // 数据长度
                canfd_data.frame.flags |= 0x20;          // 发送回显
                canfd_data.frame.data = new byte[64];   // 此成员必须new64个字节，否则无法运行，数据长度修改frame.len成员即可
                Array.Copy(data, canfd_data.frame.data, Math.Min(len, data.Length));
                Marshal.StructureToPtr(canfd_data, (IntPtr)(p2CanFDData + Marshal.SizeOf(typeof(ZLGCAN.ZCAN_TransmitFD_Data))), true);
                uint retfd = ZLGCAN.ZCAN_TransmitFD(channelhandler, p2CanFDData, 1);
                OnExecuteLog?.Invoke(this,
                    new ExecuteLogEvent()
                        { ClassName = nameof(CanBusZlg), MethodName = nameof(SendByte), LogMessage = $"发送数据结果：{retfd}" });
                Marshal.FreeHGlobal(p2CanFDData);
                return retfd == 1;
            }
            catch (Exception e)
            {
                OnException?.Invoke(this,new ExceptionEvent(){ClassName = nameof(CanBusZlg),MethodName = nameof(SendByte),ErrorMessage = e.Message});
                return false;
            }
        }
        uint MakeCanId(uint id, int eff, int rtr, int err)
        {
            uint ueff = (uint)(!!(Convert.ToBoolean(eff)) ? 1 : 0); // 0 标准帧，1 扩展帧
            uint urtr = (uint)(!!(Convert.ToBoolean(rtr)) ? 1 : 0); // 0 数据帧，1 远程帧
            uint uerr = (uint)(!!(Convert.ToBoolean(err)) ? 1 : 0); // 0 CAN帧，1 错误帧（目前只能设置为 0）
            return id | ueff << 31 | urtr << 30 | uerr << 29;
        } 
    }
}