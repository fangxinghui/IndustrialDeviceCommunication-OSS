using CoreShared;
using CoreShared.Events;
using DeviceCommunication.Abstractions;
using CoreShared;
using DeviceCommunication.Protocols.BeckOff;
using HslCommunication;
using System;
using DeviceCommunication.Models;

namespace DeviceCommunication.Protocols.Siemens
{
    internal class SiemensNet : IController
    {
        public bool IsConnected => PLCConnected;

        public event EventHandler<ExceptionEvent>? OnException;
        public event EventHandler<ExecuteLogEvent>? OnExecuteLog;
        private HslCommunication.Profinet.Siemens.SiemensS7Net? _siemensNet;
        private SiemensNetConfiguration? siemensNetConfiguration;
        private bool PLCConnected = false;

        public IConfiguration? CreateConfiguration() => siemensNetConfiguration;

        protected void WriteLog(object sender, ExecuteLogEvent e)
        {
            OnExecuteLog?.Invoke(sender, e);
        }
        protected void WriteException(object sender, ExceptionEvent e)
        {
            OnException?.Invoke(sender, e);
        }
        public bool Connect(int timeout)
        {
            try
            {
                WriteLog(this, new ExecuteLogEvent() { LogMessage = "Start connect siemens plc", ClassName = nameof(SiemensNet), MethodName = nameof(Connect) });
                PLCConnected=_siemensNet?.ConnectServer()?.IsSuccess ?? false;
                return PLCConnected;
            }
            catch (Exception ex)
            {
                WriteException(this, new ExceptionEvent { ErrorMessage = ex.Message, ClassName = nameof(SiemensNet), MethodName = nameof(Connect) });
                return false;
            }
        }

        public void Disconnect()
        {
            try
            {
                WriteLog(this, new ExecuteLogEvent() { LogMessage = "disconnect siemens plc", ClassName = nameof(SiemensNet), MethodName = nameof(Disconnect) });
                _siemensNet?.ConnectClose();
                PLCConnected = false;
            }
            catch (Exception ex)
            {
                WriteException(this, new ExceptionEvent { ErrorMessage = ex.Message, ClassName = nameof(SiemensNet), MethodName = nameof(Disconnect) });
            }
        }

        public bool Initialize<T>(T parameter)
        {
            if (parameter is SiemensNetConfiguration netConfig)
            {
                if (string.IsNullOrEmpty(netConfig.IP))
                {
                    WriteLog(this, new ExecuteLogEvent { LogMessage = "IP is null or empty.", ClassName = nameof(SiemensNet), MethodName = nameof(Initialize) });
                    return false;
                }
                siemensNetConfiguration = netConfig;

                HslCommunication.Profinet.Siemens.SiemensPLCS plcType = netConfig.PLCType switch
                {
                    SiemensPLCType.S1200 => HslCommunication.Profinet.Siemens.SiemensPLCS.S1200,
                    SiemensPLCType.S1500 => HslCommunication.Profinet.Siemens.SiemensPLCS.S1500,
                    SiemensPLCType.S300 => HslCommunication.Profinet.Siemens.SiemensPLCS.S300,
                    SiemensPLCType.S400 => HslCommunication.Profinet.Siemens.SiemensPLCS.S400,
                    SiemensPLCType.S200Smart => HslCommunication.Profinet.Siemens.SiemensPLCS.S200Smart,
                    SiemensPLCType.S200 => HslCommunication.Profinet.Siemens.SiemensPLCS.S200,
                    _ => throw new ArgumentOutOfRangeException(nameof(netConfig.PLCType), "Unsupported PLC type.")
                };
                _siemensNet = new HslCommunication.Profinet.Siemens.SiemensS7Net(plcType, netConfig.IP);
                switch (plcType)
                {
                    case HslCommunication.Profinet.Siemens.SiemensPLCS.S300:
                        _siemensNet.Rack = 0;
                        _siemensNet.Slot = 2;
                        break;
                    case HslCommunication.Profinet.Siemens.SiemensPLCS.S400:
                        _siemensNet.Rack = netConfig.Rack;
                        _siemensNet.Slot = netConfig.Slot;
                        break;
                }


                WriteLog(this, new ExecuteLogEvent { LogMessage = "ADS Client initialized successfully.", ClassName = nameof(BaseAdsClient), MethodName = nameof(Initialize) });
                return true;
            }
            return false;
        }

        public (bool success, object value) ReadValue(ControllerAddress? address)
        {
            try
            {
                if (address is null || string.IsNullOrEmpty(address.Address))
                    throw new ArgumentException("地址不能为空", nameof(address));
                dynamic operateResult;
                switch (address.DataType)
                {
                    case DataConvert.DataType.Bool:
                        operateResult = _siemensNet?.ReadBool(address.Address) ?? new OperateResult<bool>() { IsSuccess = false };
                        return (operateResult.IsSuccess, operateResult.Content);
                    case DataConvert.DataType.Byte:
                        operateResult = _siemensNet?.ReadByte(address.Address) ?? new OperateResult<byte>() { IsSuccess = false };
                        return (operateResult.IsSuccess, operateResult.Content);
                    case DataConvert.DataType.UInt16:
                        operateResult = _siemensNet?.ReadUInt16(address.Address) ?? new OperateResult<ushort>() { IsSuccess = false };
                        return (operateResult.IsSuccess, operateResult.Content);
                    case DataConvert.DataType.Int16:
                        operateResult = _siemensNet?.ReadInt16(address.Address) ?? new OperateResult<short>() { IsSuccess = false };
                        return (operateResult.IsSuccess, operateResult.Content);
                    case DataConvert.DataType.UInt32:
                        operateResult = _siemensNet?.ReadUInt32(address.Address) ?? new OperateResult<uint>() { IsSuccess = false };
                        return (operateResult.IsSuccess, operateResult.Content);
                    case DataConvert.DataType.Int32:
                        operateResult = _siemensNet?.ReadInt32(address.Address) ?? new OperateResult<int>() { IsSuccess = false };
                        return (operateResult.IsSuccess, operateResult.Content);
                    case DataConvert.DataType.Float:
                        operateResult = _siemensNet?.ReadFloat(address.Address) ?? new OperateResult<float>() { IsSuccess = false };
                        return (operateResult.IsSuccess, operateResult.Content);
                    case DataConvert.DataType.Double:
                        operateResult = _siemensNet?.ReadDouble(address.Address) ?? new OperateResult<double>() { IsSuccess = false };
                        return (operateResult.IsSuccess, operateResult.Content);
                    case DataConvert.DataType.ULong:
                        operateResult = _siemensNet?.ReadUInt64(address.Address) ?? new OperateResult<ulong>() { IsSuccess = false };
                        return (operateResult.IsSuccess, operateResult.Content);
                    case DataConvert.DataType.Long:
                        operateResult = _siemensNet?.ReadInt64(address.Address) ?? new OperateResult<long>() { IsSuccess = false };
                        return (operateResult.IsSuccess, operateResult.Content);
                    case DataConvert.DataType.String:
                        operateResult = _siemensNet?.ReadString(address.Address) ?? new OperateResult<string>() { IsSuccess = false };
                        return (operateResult.IsSuccess, operateResult.Content);
                    default:
                        throw new NotImplementedException();
                }
            }
            catch (Exception ex)
            {
                WriteException(this, new ExceptionEvent { ErrorMessage = ex.Message, ClassName = nameof(SiemensNet), MethodName = nameof(ReadValue) });
                return (false, Array.Empty<object>());
            }
        }

        public (bool success, object[] values) ReadValues<T>(ControllerAddress[]? addresses, ushort length)
        {
            try
            {
                //没有批量读取接口，暂时只能循环读取
                if (addresses is null || addresses.Length == 0)
                    throw new ArgumentException("地址列表不能为空", nameof(addresses));
                object[] values = new object[addresses.Length];
                for (int i = 0; i < addresses.Length; i++)
                {
                    var result = ReadValue(addresses[i]);
                    if (!result.success)
                        throw new Exception($"读取地址 {addresses[i].Address} 失败");
                    values[i] = result.value;
                }
                return (true, values);
            }
            catch (Exception ex)
            {
                WriteException(this, new ExceptionEvent { ErrorMessage = ex.Message, ClassName = nameof(SiemensNet), MethodName = nameof(ReadValue) });
                return (false, Array.Empty<object>());
            }
        }

        public bool WriteValue<T>(ControllerAddress? address, T value)
        {
            try
            {
                if (address is null || string.IsNullOrEmpty(address.Address))
                    throw new ArgumentException("地址不能为空", nameof(address));
                OperateResult operateResult = typeof(T) switch
                {
                    Type t when t == typeof(bool) => _siemensNet?.Write(address.Address, Convert.ToBoolean(value)),
                    Type t when t == typeof(byte) => _siemensNet?.Write(address.Address, Convert.ToByte(value)),
                    Type t when t == typeof(ushort) => _siemensNet?.Write(address.Address, Convert.ToUInt16(value)),
                    Type t when t == typeof(short) => _siemensNet?.Write(address.Address, Convert.ToInt16(value)),
                    Type t when t == typeof(uint) => _siemensNet?.Write(address.Address, Convert.ToUInt32(value)),
                    Type t when t == typeof(int) => _siemensNet?.Write(address.Address, Convert.ToInt32(value)),
                    Type t when t == typeof(float) => _siemensNet?.Write(address.Address, Convert.ToSingle(value)),
                    Type t when t == typeof(double) => _siemensNet?.Write(address.Address, Convert.ToDouble(value)),
                    Type t when t == typeof(ulong) => _siemensNet?.Write(address.Address, Convert.ToUInt64(value)),
                    Type t when t == typeof(long) => _siemensNet?.Write(address.Address, Convert.ToInt64(value)),
                    Type t when t == typeof(string) => _siemensNet?.Write(address.Address, Convert.ToString(value)),
                    _ => throw new NotImplementedException($"不支持的数据类型 {typeof(T)}")
                };
                return operateResult?.IsSuccess ?? false;
            }
            catch (Exception ex)
            {
                WriteException(this, new ExceptionEvent { ErrorMessage = ex.Message, ClassName = nameof(SiemensNet), MethodName = nameof(ReadValue) });
                return false;
            }
        }

        public bool WriteValues<T>(ControllerAddress[]? addresses, T[]? values)
        {
            try
            {
                //没有批量写入接口，暂时只能循环写入
                if (addresses is null || values is null || addresses.Length == 0 || values.Length == 0)
                    throw new ArgumentException("地址列表和值列表不能为空", nameof(addresses));
                if (addresses.Length != values.Length)
                    throw new ArgumentException("地址列表和值列表长度不一致", nameof(addresses));
                for (int i = 0; i < addresses.Length; i++)
                {
                    if (!WriteValue(addresses[i], values[i]))
                        throw new Exception($"写入地址 {addresses[i].Address} 失败");
                }
                return true;
            }
            catch (Exception ex)
            {
                WriteException(this, new ExceptionEvent { ErrorMessage = ex.Message, ClassName = nameof(SiemensNet), MethodName = nameof(ReadValue) });
                return false;
            }
        }
    }
}
