using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DeviceCommunication.Abstractions;
using CoreShared;
using CoreShared.Events;
using DeviceCommunication.Infrastructure;
using DeviceCommunication.Models;
using FluentModbus;
using IoParity = System.IO.Ports.Parity;
using IoStopBits = System.IO.Ports.StopBits;
namespace DeviceCommunication.Protocols
{
    internal enum ModbusReadArea
    {
        Coils,
        DiscreteInputs,
        InputRegisters,
        HoldingRegisters,
    }

    internal class ModbusRtu : IController, IModbus
    {
        public event EventHandler<ExceptionEvent>? OnException;
        public event EventHandler<ExecuteLogEvent>? OnExecuteLog;
        protected virtual IModbusClientAdapter? modbusclient { get; set; } = null;
        public virtual bool IsConnected => modbusclient is FluentModbusRtuClientAdapter client && client.IsOpen;
        private SerialPortConfiguration? _serialConfig;
        /// <summary>
        /// 首地址是否从0开始
        /// </summary>
        public bool IsAddressStartWithZero { get; set; } = true;
        /// <summary>
        /// 站号
        /// </summary>
        public byte StationId { get; set; } = 0x01;
        /// <summary>
        /// 数据格式
        /// </summary>
        public ModbusDataType ModbusDataType { get; set; } = ModbusDataType.ABCD;

        public virtual IConfiguration? CreateConfiguration() => _serialConfig;

        public virtual bool Initialize<T>(T parameter)
        {
            if (parameter is SerialPortConfiguration config)
            {
                _serialConfig = config;
                if (config is ModbusRtuConfiguration modbusConfig)
                {
                    StationId = modbusConfig.StationId;
                    ModbusDataType = modbusConfig.ModbusDataType;
                    IsAddressStartWithZero = modbusConfig.IsAddressStartWithZero;
                }

                var stopBits = _serialConfig.StopBits switch
                {
                    SerialParameters.SerialStopBits.Two => IoStopBits.Two,
                    SerialParameters.SerialStopBits.OnePointFive => IoStopBits.OnePointFive,
                    _ => IoStopBits.One
                };

                var parity = _serialConfig.Parity switch
                {
                    SerialParameters.SerialParity.Odd => IoParity.Odd,
                    SerialParameters.SerialParity.Even => IoParity.Even,
                    SerialParameters.SerialParity.Mark => IoParity.Mark,
                    SerialParameters.SerialParity.Space => IoParity.Space,
                    _ => IoParity.None
                };

                var portName = ControllerParameterParser.NormalizeSerialPortName(_serialConfig.PortName ?? "COM1");
                _serialConfig.PortName = portName;

                var baudRate = _serialConfig.BaudRate > 0 ? _serialConfig.BaudRate : 9600;
                var dataBits = _serialConfig.DataBits is 7 or 8 ? _serialConfig.DataBits : 8;

                if (modbusclient is FluentModbusRtuClientAdapter existing &&
                    existing.IsOpen &&
                    existing.Matches(portName, baudRate, dataBits, parity, stopBits))
                {
                    return true;
                }

                modbusclient?.Close();
                modbusclient = new FluentModbusRtuClientAdapter(baudRate, dataBits, parity, stopBits);
                return true;
            }
            else
            {
                return false;
            }
        }

        public virtual bool Connect(int timeout = 5000)
        {
            if (modbusclient is not FluentModbusRtuClientAdapter client)
            {
                OnException?.Invoke(this, new ExceptionEvent
                {
                    ErrorMessage = "Modbus RTU client is not initialized.",
                    ClassName = nameof(ModbusRtu),
                    MethodName = nameof(Connect)
                });
                return false;
            }

            client.ReadTimeout = timeout;
            client.WriteTimeout = timeout;

            if (client.IsOpen)
                return true;

            try
            {
                var portName = ControllerParameterParser.NormalizeSerialPortName(_serialConfig?.PortName ?? string.Empty);
                if (string.IsNullOrWhiteSpace(portName))
                    throw new InvalidOperationException("Serial port name is empty.");

                client.Connect(portName);
                return client.IsOpen;
            }
            catch (Exception ex)
            {
                if (!client.IsOpen)
                {
                    OnException?.Invoke(this, new ExceptionEvent
                    {
                        ErrorMessage = ex.Message,
                        ClassName = nameof(ModbusRtu),
                        MethodName = nameof(Connect)
                    });
                }

                return client.IsOpen;
            }
        }

        public virtual void Disconnect()
        {
            modbusclient?.Close();
            OnExecuteLog?.Invoke(this, new ExecuteLogEvent { LogMessage = "Modbus RTU disconnected", ClassName = nameof(ModbusRtu), MethodName = nameof(Disconnect) });
        }
        
        public (bool success,object value) ReadValue(ControllerAddress? address)
        {
            if (address is null)
            {
                OnExecuteLog?.Invoke(this, new ExecuteLogEvent { LogMessage = "ReadValue failed: address is null", ClassName = nameof(ModbusRtu), MethodName = nameof(ReadValue) });
                return (false,null!);
            }

            if (string.IsNullOrEmpty(address.Address))
            {
                OnExecuteLog?.Invoke(this, new ExecuteLogEvent { LogMessage = "ReadValue failed: address is empty", ClassName = nameof(ModbusRtu), MethodName = nameof(ReadValue) });
                return (false,null!);
            }
            if (!IsConnected)
            {
                OnExecuteLog?.Invoke(this, new ExecuteLogEvent { LogMessage = "ReadValue failed: Modbus client is not connected", ClassName = nameof(ModbusRtu), MethodName = nameof(ReadValue) });
                return (false,null!);
            }

            object value = new object();
            //输入状态或者输入寄存器
            if (!int.TryParse(address.Address, out int startingAddress))
            {
                //OnExecuteLog?.Invoke(this, new ExecuteLogEvent { LogMessage = $"ReadValue: address '{address.Address}' is not a valid integer, trying to match with pattern", ClassName = nameof(ModbusRtu), MethodName = nameof(ReadValue) });
                var result=AddressMatch(address.Address);
                if (!result.IsValid)
                {
                    OnExecuteLog?.Invoke(this, new ExecuteLogEvent { LogMessage = $"ReadValue failed: address '{address.Address}' does not match expected pattern", ClassName = nameof(ModbusRtu), MethodName = nameof(ReadValue) });
                    return (false,null!);
                }
                //OnExecuteLog?.Invoke(this, new ExecuteLogEvent { LogMessage = $"ReadValue: address '{address.Address}' matched pattern with Part1={result.Part1}, Part2={result.Part2}", ClassName = nameof(ModbusRtu), MethodName = nameof(ReadValue) });
                try
                {
                    value = ReadSingleValue(result.Part2, ResolveModbusReadArea(result.Part1, address))!;
                    return (true, value!);
                }
                catch (Exception e)
                {
                   OnException?.Invoke(this, new ExceptionEvent
                   {
                       ErrorMessage = FormatReadError(address, ResolveModbusReadArea(result.Part1, address), e.Message),
                       ClassName = nameof(ModbusRtu),
                       MethodName = nameof(ReadValue)
                   });
                   return (false,null!);
                }

            }

            try
            {
                var readArea = ResolveModbusReadArea(0, address);
                value = ReadSingleValue(startingAddress, readArea)!;
                return (true, value);
            }
            catch (Exception ex)
            {
                OnException?.Invoke(this, new ExceptionEvent
                {
                    ErrorMessage = FormatReadError(address, ResolveModbusReadArea(0, address), ex.Message),
                    ClassName = nameof(ModbusRtu),
                    MethodName = nameof(ReadValue)
                });
                return (false,null!);
            }
            
        }

        public bool WriteValue<T>(ControllerAddress? address, T value)
        {
            if (address is null)
            {
                OnExecuteLog?.Invoke(this, new ExecuteLogEvent { LogMessage = "WriteValue failed: address is null", ClassName = nameof(ModbusRtu), MethodName = nameof(WriteValue) });
                return false;
            }
            
            //输入状态或者输入寄存器
            if (!int.TryParse(address.Address, out int startingAddress))
            {
                var result=AddressMatch(address.Address!);
                if (result.IsValid)
                {
                    if (result.Part1 == 2 || result.Part1 == 4)
                    {
                        OnExecuteLog?.Invoke(this, new ExecuteLogEvent { LogMessage = $"WriteValue: address '{address.Address}' Input forbid write value", ClassName = nameof(ModbusRtu), MethodName = nameof(WriteValue) });
                        return false;
                    }
                    startingAddress = result.Part2;
                }
            }

            try
            {
                if (modbusclient is null)
                {
                    OnExecuteLog?.Invoke(this, new ExecuteLogEvent { LogMessage = $"WriteValue: modbusclient is null (client not initialized)", ClassName = nameof(ModbusRtu), MethodName = nameof(WriteValue) });
                    return false;
                }
                switch (value)
                {
                    case bool v:
                        modbusclient.WriteSingleCoil(StationId,startingAddress, v);
                        break;
                    default:
                        unchecked
                        {
                            modbusclient.WriteSingleRegister(StationId,startingAddress, (short)Convert.ToUInt16(value));
                        }
                        break;
                }
                OnExecuteLog?.Invoke(this, new ExecuteLogEvent { LogMessage = $"WriteValue: address '{address.Address}' write value '{value}' success", ClassName = nameof(ModbusRtu), MethodName = nameof(WriteValue) });
                return true;
            }
            catch (Exception e)
            {
                OnException?.Invoke(this, new ExceptionEvent { ErrorMessage = e.Message, ClassName = nameof(ModbusRtu), MethodName = nameof(WriteValue) });
                return false;
            }
   
        }

        public (bool success,object[]values) ReadValues<T>(ControllerAddress[]? addresses, ushort length)
        {
            if (addresses is null)
            {
                OnExecuteLog?.Invoke(this, new ExecuteLogEvent { LogMessage = "ReadValues failed: addresses is null", ClassName = nameof(ModbusRtu), MethodName = nameof(ReadValues) });
                return (false, null!);
            }

            if (addresses.Length == 0)
            {
                OnExecuteLog?.Invoke(this, new ExecuteLogEvent { LogMessage = "ReadValues failed: addresses length is 0", ClassName = nameof(ModbusRtu), MethodName = nameof(ReadValues) });
                return (false, null!);
            }
            if (length == 0)
            {
                OnExecuteLog?.Invoke(this, new ExecuteLogEvent { LogMessage = "ReadValues failed: length is 0", ClassName = nameof(ModbusRtu), MethodName = nameof(ReadValues) });
                return (false, null!);
            }
            //输入状态或者输入寄存器
            var result=AddressSort(addresses);
            if (!result.IsValid)
            {
                OnExecuteLog?.Invoke(this, new ExecuteLogEvent { LogMessage = "ReadValues failed: address sorting failed", ClassName = nameof(ModbusRtu), MethodName = nameof(ReadValues) });
                return (false,null!);
            }
            object[] values=new object[length];
            int startAddress=result.StartAddress;
            var result_2=AddressMatch(addresses[result.index].Address!);
            if (result_2.IsValid)
            {
                try
                {
                    var readArea = ResolveModbusReadArea(result_2.Part1, addresses[result.index]);
                    values = ReadMultipleValues(startAddress, length, readArea, result.dataType);
                    OnExecuteLog?.Invoke(this, new ExecuteLogEvent { LogMessage = $"ReadValues: address '{addresses[result.index].Address}' read values success", ClassName = nameof(ModbusRtu), MethodName = nameof(ReadValues) });
                    return (true, values);
                }
                catch (Exception ex)
                {
                    OnException?.Invoke(this, new ExceptionEvent
                    {
                        ErrorMessage = FormatReadError(addresses[result.index], ResolveModbusReadArea(result_2.Part1, addresses[result.index]), ex.Message),
                        ClassName = nameof(ModbusRtu),
                        MethodName = nameof(ReadValues)
                    });
                    return (false,null!);   
                }
               
            }

            try
            {
                var readArea = ResolveModbusReadArea(0, addresses[result.index]);
                values = ReadMultipleValues(startAddress, length, readArea, result.dataType);
                OnExecuteLog?.Invoke(this, new ExecuteLogEvent { LogMessage = $"ReadValues: address '{addresses[result.index].Address}' read values success", ClassName = nameof(ModbusRtu), MethodName = nameof(ReadValues) });
                return (true, values);
            }
            catch (Exception ex)
            {
                OnException?.Invoke(this, new ExceptionEvent
                {
                    ErrorMessage = FormatReadError(addresses[result.index], ResolveModbusReadArea(0, addresses[result.index]), ex.Message),
                    ClassName = nameof(ModbusRtu),
                    MethodName = nameof(ReadValues)
                });
                return (false, null!);
            }

        }

        public bool WriteValues<T>(ControllerAddress[]? addresses, T[]? values)
        {
            if (addresses is null || values is null)
            {
                OnExecuteLog?.Invoke(this, new ExecuteLogEvent { LogMessage = "WriteValues failed: addresses or values is null", ClassName = nameof(ModbusRtu), MethodName = nameof(WriteValues) });
                return false;
            }

            if (addresses.Length != values.Length)
            {
                OnExecuteLog?.Invoke(this, new ExecuteLogEvent { LogMessage = "WriteValues failed: addresses length does not match values length", ClassName = nameof(ModbusRtu), MethodName = nameof(WriteValues) });
                return false;
            }
            //输入状态或者输入寄存器
            var result=AddressSort(addresses);
            if (!result.IsValid)
            {
                OnExecuteLog?.Invoke(this, new ExecuteLogEvent { LogMessage = "WriteValues failed: address sorting failed", ClassName = nameof(ModbusRtu), MethodName = nameof(WriteValues) });
                return false;
            }
            int startAddress=result.StartAddress;
            var result_2=AddressMatch(addresses[result.index].Address!);
            if (result_2.IsValid && (result_2.Part1 == 2 || result_2.Part1 == 4))
            {
                OnExecuteLog?.Invoke(this, new ExecuteLogEvent { LogMessage = $"WriteValues: address '{addresses[result.index].Address}' Input forbid write values", ClassName = nameof(ModbusRtu), MethodName = nameof(WriteValues) });
                return false;
            }

            try
            {
                switch (result.dataType)
                {
                    case DataConvert.DataType.Bool:
                        modbusclient?.WriteMultipleCoils(StationId,startAddress, values.Select(v=>Convert.ToBoolean(v)).ToArray());
                        break;
                    case DataConvert.DataType.UInt16:
                    case DataConvert.DataType.Int16:
                        unchecked
                        {
                            modbusclient?.WriteMultipleRegisters(StationId,startAddress, values.Select(v=>(short)Convert.ToUInt16(v)).ToArray());
                        }
                        break;
                    case DataConvert.DataType.UInt32:
                    case DataConvert.DataType.Int32:
                    case DataConvert.DataType.Float:
                    case DataConvert.DataType.Double:
                    case DataConvert.DataType.Long:
                        modbusclient?.WriteMultipleRegisters(StationId,startAddress, values.Select(v=>Convert.ToInt32(v)).ToArray());
                        break;
                }
                OnExecuteLog?.Invoke(this, new ExecuteLogEvent { LogMessage = $"WriteValues: address '{addresses[result.index].Address}' write values success", ClassName = nameof(ModbusRtu), MethodName = nameof(WriteValues) });
                return true;
            }
            catch (Exception ex)
            {
                OnException?.Invoke(this, new ExceptionEvent { ErrorMessage = ex.Message, ClassName = nameof(ModbusRtu), MethodName = nameof(WriteValues) });
                return false;
            }

        }
        /// <summary>
        /// 根据地址表类型或 x=N;M 前缀选择 Modbus 读区。
        /// x=1;M 线圈，x=2;M 离散输入，x=3;M 保持寄存器，x=4;M 输入寄存器。
        /// </summary>
        protected virtual ModbusReadArea ResolveModbusReadArea(int addressPrefix, ControllerAddress address)
        {
            if (addressPrefix > 0)
            {
                return addressPrefix switch
                {
                    1 => ModbusReadArea.Coils,
                    2 => ModbusReadArea.DiscreteInputs,
                    3 => ModbusReadArea.HoldingRegisters,
                    4 => ModbusReadArea.InputRegisters,
                    _ => ModbusReadArea.HoldingRegisters,
                };
            }

            if (address.DataType == DataConvert.DataType.Bool)
            {
                return address.AddressType switch
                {
                    ControllerAddressType.DigitalInput => ModbusReadArea.DiscreteInputs,
                    ControllerAddressType.DigitalOutput => ModbusReadArea.Coils,
                    _ => ModbusReadArea.Coils,
                };
            }

            return address.AddressType switch
            {
                ControllerAddressType.AnalogInput => ModbusReadArea.InputRegisters,
                ControllerAddressType.DigitalInput => ModbusReadArea.HoldingRegisters,
                ControllerAddressType.DigitalOutput => ModbusReadArea.HoldingRegisters,
                _ => ModbusReadArea.HoldingRegisters,
            };
        }

        private object ReadSingleValue(int startingAddress, ModbusReadArea readArea)
        {
            if (modbusclient is null)
                throw new InvalidOperationException("Modbus client is not initialized.");

            return readArea switch
            {
                ModbusReadArea.Coils => modbusclient.ReadCoils(StationId, startingAddress, 1).ToArray()[0],
                ModbusReadArea.DiscreteInputs => modbusclient.ReadDiscreteInputs(StationId, startingAddress, 1).ToArray()[0],
                ModbusReadArea.InputRegisters => modbusclient.ReadInputRegisters(StationId, (ushort)startingAddress, 1).ToArray()[0],
                _ => modbusclient.ReadHoldingRegisters(StationId, (ushort)startingAddress, 1).ToArray()[0],
            };
        }

        private object[] ReadMultipleValues(int startAddress, ushort length, ModbusReadArea readArea, DataConvert.DataType dataType)
        {
            if (modbusclient is null)
                throw new InvalidOperationException("Modbus client is not initialized.");

            return readArea switch
            {
                ModbusReadArea.Coils => modbusclient.ReadCoils(StationId, startAddress, length).ToArray().Select(v => (object)v).ToArray(),
                ModbusReadArea.DiscreteInputs => modbusclient.ReadDiscreteInputs(StationId, startAddress, length).ToArray().Select(v => (object)v).ToArray(),
                ModbusReadArea.InputRegisters => modbusclient.ReadInputRegisters(StationId, (ushort)startAddress, length).ToArray().Select(v => (object)v).ToArray(),
                _ => modbusclient.ReadHoldingRegisters(StationId, (ushort)startAddress, length).ToArray().Select(v => (object)v).ToArray(),
            };
        }

        private static string FormatReadError(ControllerAddress address, ModbusReadArea readArea, string message)
        {
            var functionCode = readArea switch
            {
                ModbusReadArea.Coils => "FC01 ReadCoils",
                ModbusReadArea.DiscreteInputs => "FC02 ReadDiscreteInputs",
                ModbusReadArea.InputRegisters => "FC04 ReadInputRegisters",
                _ => "FC03 ReadHoldingRegisters",
            };

            return $"{message} [Signal={address.Name}, Address={address.Address}, Type={address.AddressType}, DataType={address.DataType}, {functionCode}]";
        }

        /// <summary>
        /// 地址匹配 地址可是x=2;100 代表离散输入寄存器100地址 x=4;100 代表输入寄存器100地址
        /// </summary>
        /// <param name="address"></param>
        /// <returns></returns>
        protected virtual (bool IsValid, int Part1, int Part2) AddressMatch(string address)
        {
            Regex addressRegex = new Regex(@"^x=\d+;\d+$");
            if(string.IsNullOrEmpty(address)) return (false,0,0);
            var match = addressRegex.Match(address.Trim());
            if (!match.Success) return (false,0,0);
            var parts = address.Split(';');
            if (parts.Length != 2) return (false,0,0);
            if (!int.TryParse(parts[0].Substring(2), out int part1)) return (false,0,0);
            if (!int.TryParse(parts[1], out int part2)) return (false,0,0);
            return (true, part1, part2);
        }
        /// <summary>
        /// 地址排序
        /// </summary>
        /// <param name="addresses"></param>
        /// <returns></returns>
        protected virtual (bool IsValid,int StartAddress,DataConvert.DataType dataType,int index) AddressSort(ControllerAddress[]? addresses)
        {
            if(addresses is null|| addresses.Length==0) return (false,0,DataConvert.DataType.Bool,0);
            List<int>addressList=new List<int>();
            List<DataConvert.DataType>dataTypes=new List<DataConvert.DataType>();
            foreach (var controllerAddress in addresses)
            {
                if (controllerAddress is null || string.IsNullOrEmpty(controllerAddress.Address))
                {
                    return (false,0,DataConvert.DataType.Bool,0);
                }
                if(int.TryParse(controllerAddress.Address,out int address))
                {
                    addressList.Add(address);
                    dataTypes.Add(controllerAddress.DataType);
                }
                else
                {
                    var result=AddressMatch(controllerAddress.Address);
                    if(!result.IsValid) return (false,0,DataConvert.DataType.Bool,0);
                    addressList.Add(result.Part2);
                    dataTypes.Add(controllerAddress.DataType);
                }
            }
            int startAddress = addressList.Min();
            int index=addressList.FindIndex(p => p == startAddress);
            return (true,startAddress,dataTypes[index],index);
        }
        #region 16位整数转换
        /// <summary>
        /// 从单个寄存器读取Int16
        /// </summary>
        /// <param name="registers">寄存器数组</param>
        /// <param name="startIndex">起始寄存器索引</param>
        /// <returns>Int16值</returns>
        private short GetInt16(ushort[] registers, int startIndex)
        {
            if (registers == null || registers.Length <= startIndex)
                throw new ArgumentException("无效的寄存器数组或索引");

            byte[] bytes = GetBytesFromRegisters(registers, startIndex, 1);
            return BitConverter.ToInt16(bytes, 0);
        }

        /// <summary>
        /// 从单个寄存器读取UInt16
        /// </summary>
        /// <param name="registers">寄存器数组</param>
        /// <param name="startIndex">起始寄存器索引</param>
        /// <returns>UInt16值</returns>
        private ushort GetUInt16(ushort[] registers, int startIndex)
        {
            if (registers == null || registers.Length <= startIndex)
                throw new ArgumentException("无效的寄存器数组或索引");

            byte[] bytes = GetBytesFromRegisters(registers, startIndex, 1);
            return BitConverter.ToUInt16(bytes, 0);
        }

        #endregion

        #region 32位整数转换

        /// <summary>
        /// 从两个寄存器读取Int32
        /// </summary>
        /// <param name="registers">寄存器数组</param>
        /// <param name="startIndex">起始寄存器索引</param>
        /// <returns>Int32值</returns>
        private int GetInt32(ushort[] registers, int startIndex)
        {
            if (registers == null || registers.Length <= startIndex + 1)
                throw new ArgumentException("寄存器数组长度不足，需要至少2个寄存器");

            byte[] bytes = GetBytesFromRegisters(registers, startIndex, 2);
            return BitConverter.ToInt32(bytes, 0);
        }

        /// <summary>
        /// 从两个寄存器读取UInt32
        /// </summary>
        /// <param name="registers">寄存器数组</param>
        /// <param name="startIndex">起始寄存器索引</param>
        /// <returns>UInt32值</returns>
        private uint GetUInt32(ushort[] registers, int startIndex)
        {
            if (registers == null || registers.Length <= startIndex + 1)
                throw new ArgumentException("寄存器数组长度不足，需要至少2个寄存器");

            byte[] bytes = GetBytesFromRegisters(registers, startIndex, 2);
            return BitConverter.ToUInt32(bytes, 0);
        }

        #endregion

        #region 32位浮点数转换

        /// <summary>
        /// 从两个寄存器读取Float
        /// </summary>
        /// <param name="registers">寄存器数组</param>
        /// <param name="startIndex">起始寄存器索引</param>
        /// <returns>Float值</returns>
        private float GetFloat(ushort[] registers, int startIndex)
        {
            if (registers == null || registers.Length <= startIndex + 1)
                throw new ArgumentException("寄存器数组长度不足，需要至少2个寄存器");

            byte[] bytes = GetBytesFromRegisters(registers, startIndex, 2);
            return BitConverter.ToSingle(bytes, 0);
        }

        #endregion

        #region 64位整数转换

        /// <summary>
        /// 从四个寄存器读取Int64
        /// </summary>
        /// <param name="registers">寄存器数组</param>
        /// <param name="startIndex">起始寄存器索引</param>
        /// <returns>Int64值</returns>
        private long GetInt64(ushort[] registers, int startIndex)
        {
            if (registers == null || registers.Length <= startIndex + 3)
                throw new ArgumentException("寄存器数组长度不足，需要至少4个寄存器");

            byte[] bytes = GetBytesFromRegisters(registers, startIndex, 4);
            return BitConverter.ToInt64(bytes, 0);
        }

        /// <summary>
        /// 从四个寄存器读取UInt64
        /// </summary>
        /// <param name="registers">寄存器数组</param>
        /// <param name="startIndex">起始寄存器索引</param>
        /// <returns>UInt64值</returns>
        private ulong GetUInt64(ushort[] registers, int startIndex)
        {
            if (registers == null || registers.Length <= startIndex + 3)
                throw new ArgumentException("寄存器数组长度不足，需要至少4个寄存器");

            byte[] bytes = GetBytesFromRegisters(registers, startIndex, 4);
            return BitConverter.ToUInt64(bytes, 0);
        }

        #endregion

        #region 64位双精度浮点数转换

        /// <summary>
        /// 从四个寄存器读取Double
        /// </summary>
        /// <param name="registers">寄存器数组</param>
        /// <param name="startIndex">起始寄存器索引</param>
        /// <returns>Double值</returns>
        private double GetDouble(ushort[] registers, int startIndex)
        {
            if (registers == null || registers.Length <= startIndex + 3)
                throw new ArgumentException("寄存器数组长度不足，需要至少4个寄存器");

            byte[] bytes = GetBytesFromRegisters(registers, startIndex, 4);
            return BitConverter.ToDouble(bytes, 0);
        }
        #endregion
        #region 批量转换

        /// <summary>
        /// 批量读取Int32数组
        /// </summary>
        /// <param name="registers">寄存器数组</param>
        /// <param name="startIndex">起始寄存器索引</param>
        /// <param name="count">要读取的Int32数量</param>
        /// <returns>Int32数组</returns>
        private int[] GetInt32Array(ushort[] registers, int startIndex, int count)
        {
            if (registers == null || registers.Length < startIndex + count * 2)
                throw new ArgumentException("寄存器数组长度不足");

            int[] result = new int[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = GetInt32(registers, startIndex + i * 2);
            }
            return result;
        }

        /// <summary>
        /// 批量读取Float数组
        /// </summary>
        /// <param name="registers">寄存器数组</param>
        /// <param name="startIndex">起始寄存器索引</param>
        /// <param name="count">要读取的Float数量</param>
        /// <returns>Float数组</returns>
        private float[] GetFloatArray(ushort[] registers, int startIndex, int count)
        {
            if (registers == null || registers.Length < startIndex + count * 2)
                throw new ArgumentException("寄存器数组长度不足");

            float[] result = new float[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = GetFloat(registers, startIndex + i * 2);
            }
            return result;
        }

        #endregion
        #region 写入寄存器

        /// <summary>
        /// 将Int16值写入寄存器
        /// </summary>
        public ushort[] SetInt16(short value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            return BytesToRegisters(bytes, 1);
        }

        /// <summary>
        /// 将Int32值写入寄存器
        /// </summary>
        public ushort[] SetInt32(int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            return BytesToRegisters(bytes, 2);
        }

        /// <summary>
        /// 将Float值写入寄存器
        /// </summary>
        public ushort[] SetFloat(float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            return BytesToRegisters(bytes, 2);
        }
        /// <summary>
        /// 将字节数组转换为寄存器数组
        /// </summary>
        private ushort[] BytesToRegisters(byte[] bytes, int expectedRegisterCount)
        {
            // 根据系统字节序调整
            byte[] systemBytes = (byte[])bytes.Clone();
            if (NeedSystemEndianConversion(ModbusDataType))
            {
                Array.Reverse(systemBytes);
            }

            // 根据寄存器顺序调整
            byte[] orderedBytes = ApplyInverseRegisterOrder(systemBytes, expectedRegisterCount,ModbusDataType);

            // 转换为寄存器
            ushort[] registers = new ushort[expectedRegisterCount];
            for (int i = 0; i < expectedRegisterCount; i++)
            {
                registers[i] = (ushort)((orderedBytes[i * 2] << 8) | orderedBytes[i * 2 + 1]);
            }

            return registers;
        }
        /// <summary>
        /// 应用逆寄存器顺序转换（用于写入）
        /// </summary>
        private byte[] ApplyInverseRegisterOrder(byte[] bytes, int registerCount,ModbusDataType _registerOrder)
        {
            byte[] result = new byte[bytes.Length];

            switch (_registerOrder)
            {
                case ModbusDataType.ABCD:
                    Array.Copy(bytes, result, bytes.Length);
                    break;

                case ModbusDataType.BADC:
                    for (int i = 0; i < registerCount; i++)
                    {
                        result[i * 2] = bytes[i * 2 + 1];
                        result[i * 2 + 1] = bytes[i * 2];
                    }
                    break;

                case ModbusDataType.CDAB:
                    for (int i = 0; i < registerCount; i++)
                    {
                        int targetIndex = (registerCount - 1 - i) * 2;
                        result[targetIndex] = bytes[i * 2];
                        result[targetIndex + 1] = bytes[i * 2 + 1];
                    }
                    break;

                case ModbusDataType.DCBA:
                    for (int i = 0; i < registerCount * 2; i++)
                    {
                        result[i] = bytes[registerCount * 2 - 1 - i];
                    }
                    break;
            }

            return result;
        }

        #endregion
        private byte[] GetBytesFromRegisters(ushort[] registers, int startIndex, int registerCount)
        {
            // 首先获取原始字节
            byte[] rawBytes = new byte[registerCount * 2];
            for (int i = 0; i < registerCount; i++)
            {
                ushort regValue = registers[startIndex + i];
                // 每个寄存器按大端序放入字节数组（Modbus协议规定寄存器是大端传输）
                rawBytes[i * 2] = (byte)(regValue >> 8);     // 高字节
                rawBytes[i * 2 + 1] = (byte)(regValue & 0xFF); // 低字节
            }

            // 根据寄存器顺序调整
            byte[] orderedBytes = ApplyRegisterOrder(rawBytes, registerCount,ModbusDataType);

            // 根据系统字节序调整（如果需要）
            if (NeedSystemEndianConversion(ModbusDataType))
            {
                Array.Reverse(orderedBytes);
            }

            return orderedBytes;
        }
      
        private int GetInt32(ushort[] registers, int startIndex,ModbusDataType modbusDataType)
        {
            if (registers == null || registers.Length <= startIndex + 1)
                throw new ArgumentException("寄存器数组长度不足，需要至少2个寄存器");

            byte[] bytes = GetBytesFromRegisters(registers, startIndex, 2,modbusDataType);
            return BitConverter.ToInt32(bytes, 0);
        }
        /// <summary>
        /// 将寄存器数组转换为字节数组，并根据指定的寄存器顺序和系统字节序进行调整
        /// </summary>
        private byte[] GetBytesFromRegisters(ushort[] registers, int startIndex, int registerCount,ModbusDataType modbusDataType)
        {
            // 首先获取原始字节
            byte[] rawBytes = new byte[registerCount * 2];
            for (int i = 0; i < registerCount; i++)
            {
                ushort regValue = registers[startIndex + i];
                // 每个寄存器按大端序放入字节数组（Modbus协议规定寄存器是大端传输）
                rawBytes[i * 2] = (byte)(regValue >> 8);     // 高字节
                rawBytes[i * 2 + 1] = (byte)(regValue & 0xFF); // 低字节
            }

            // 根据寄存器顺序调整
            byte[] orderedBytes = ApplyRegisterOrder(rawBytes, registerCount,modbusDataType);

            // 根据系统字节序调整（如果需要）
            if (NeedSystemEndianConversion(modbusDataType))
            {
                Array.Reverse(orderedBytes);
            }

            return orderedBytes;
        }
        /// <summary>
        /// 判断是否需要进行系统字节序转换
        /// </summary>
        private bool NeedSystemEndianConversion(ModbusDataType registerOrder)
        {
            // 如果系统是小端，但我们需要的是大端数据，则需要转换
            // 如果系统是大端，但我们需要的是小端数据，则需要转换
            bool targetIsLittleEndian = (registerOrder == ModbusDataType.DCBA || registerOrder == ModbusDataType.BADC);
            return BitConverter.IsLittleEndian!=targetIsLittleEndian;
        }
        private byte[] ApplyRegisterOrder(byte[] rawBytes, int registerCount,ModbusDataType modbusDataType)
        {
            byte[] result = new byte[rawBytes.Length];

            switch (modbusDataType)
            {
                case ModbusDataType.ABCD:
                    // 大端模式：不需要改变
                    Array.Copy(rawBytes, result, rawBytes.Length);
                    break;

                case ModbusDataType.BADC:
                    // 字节交换：每个寄存器内的两个字节交换
                    for (int i = 0; i < registerCount; i++)
                    {
                        result[i * 2] = rawBytes[i * 2 + 1];     // 低字节
                        result[i * 2 + 1] = rawBytes[i * 2];     // 高字节
                    }
                    break;

                case ModbusDataType.CDAB:
                    // 字交换：交换寄存器的顺序，但保持每个寄存器内的字节顺序
                    for (int i = 0; i < registerCount; i++)
                    {
                        int sourceIndex = (registerCount - 1 - i) * 2;
                        result[i * 2] = rawBytes[sourceIndex];
                        result[i * 2 + 1] = rawBytes[sourceIndex + 1];
                    }
                    break;

                case ModbusDataType.DCBA:
                    // 小端模式：完全反转所有字节
                    for (int i = 0; i < registerCount * 2; i++)
                    {
                        result[i] = rawBytes[registerCount * 2 - 1 - i];
                    }
                    break;
            }

            return result;
        }
        #region 读取字符串 
        /// <summary>
        /// 从寄存器读取字符串（基本方法）
        /// </summary>
        /// <param name="registers">寄存器数组</param>
        /// <param name="startIndex">起始寄存器索引</param>
        /// <param name="registerCount">要读取的寄存器数量</param>
        /// <param name="encoding">字符串编码格式</param>
        /// <returns>读取的字符串</returns>
        private string GetString(ushort[] registers, int startIndex, int registerCount, DataConvert.StringEncoding encoding = DataConvert.StringEncoding.ASCII)
        {
            if (registers == null)
                throw new ArgumentNullException(nameof(registers));
            
            if (startIndex < 0 || startIndex >= registers.Length)
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            
            if (registerCount <= 0 || startIndex + registerCount > registers.Length)
                throw new ArgumentException("寄存器数量无效或超出范围");

            // 获取字节数据
            byte[] bytes = GetBytesFromRegisters(registers, startIndex, registerCount);
            
            // 根据编码格式转换为字符串
            return DecodeBytesToString(bytes, encoding);
        }

        /// <summary>
        /// 从寄存器读取固定长度字符串（自动去除末尾的空字符和空格）
        /// </summary>
        /// <param name="registers">寄存器数组</param>
        /// <param name="startIndex">起始寄存器索引</param>
        /// <param name="registerCount">要读取的寄存器数量</param>
        /// <param name="encoding">字符串编码格式</param>
        /// <param name="trimNull">是否去除末尾的空字符</param>
        /// <param name="trimSpace">是否去除末尾的空格</param>
        /// <returns>读取的字符串</returns>
        private string GetFixedLengthString(ushort[] registers, int startIndex, int registerCount, 
            DataConvert.StringEncoding encoding = DataConvert.StringEncoding.ASCII, bool trimNull = true, bool trimSpace = true)
        {
            string result = GetString(registers, startIndex, registerCount, encoding);
            
            if (trimNull)
            {
                // 去除末尾的空字符
                int nullIndex = result.IndexOf('\0');
                if (nullIndex >= 0)
                    result = result.Substring(0, nullIndex);
            }
            
            if (trimSpace)
            {
                result = result.TrimEnd();
            }
            
            return result;
        }

        /// <summary>
        /// 从寄存器读取以空字符结尾的字符串
        /// </summary>
        /// <param name="registers">寄存器数组</param>
        /// <param name="startIndex">起始寄存器索引</param>
        /// <param name="maxRegisterCount">最大读取寄存器数量</param>
        /// <param name="encoding">字符串编码格式</param>
        /// <returns>读取的字符串</returns>
        private string GetNullTerminatedString(ushort[] registers, int startIndex, int maxRegisterCount, 
            DataConvert.StringEncoding encoding = DataConvert.StringEncoding.ASCII)
        {
            if (registers == null)
                throw new ArgumentNullException(nameof(registers));
            
            if (startIndex < 0 || startIndex >= registers.Length)
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            
            // 查找空字符
            int registerCount = 0;
            byte[] allBytes = new byte[maxRegisterCount * 2];
            
            for (int i = 0; i < maxRegisterCount && startIndex + i < registers.Length; i++)
            {
                ushort regValue = registers[startIndex + i];
                byte highByte = (byte)(regValue >> 8);
                byte lowByte = (byte)(regValue & 0xFF);
                
                allBytes[i * 2] = highByte;
                allBytes[i * 2 + 1] = lowByte;
                
                registerCount++;
                
                // 检查是否遇到空字符（根据编码格式）
                if (IsNullTerminator(highByte, lowByte, encoding))
                    break;
            }
            
            // 获取字节数组
            byte[] bytes = new byte[registerCount * 2];
            Array.Copy(allBytes, bytes, bytes.Length);
            
            // 转换为字符串
            string result = DecodeBytesToString(bytes, encoding);
            
            // 去除空字符及其后面的内容
            int nullIndex = result.IndexOf('\0');
            if (nullIndex >= 0)
                result = result.Substring(0, nullIndex);
            
            return result;
        }

        /// <summary>
        /// 从寄存器读取带长度前缀的字符串
        /// </summary>
        /// <param name="registers">寄存器数组</param>
        /// <param name="startIndex">起始寄存器索引</param>
        /// <param name="encoding">字符串编码格式</param>
        /// <param name="lengthIsBytes">长度前缀是字节数还是寄存器数</param>
        /// <returns>读取的字符串</returns>
        private string GetLengthPrefixedString(ushort[] registers, int startIndex, 
            DataConvert.StringEncoding encoding = DataConvert.StringEncoding.ASCII, bool lengthIsBytes = true)
        {
            if (registers == null)
                throw new ArgumentNullException(nameof(registers));
            
            if (startIndex < 0 || startIndex >= registers.Length)
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            
            // 读取长度（第一个寄存器）
            ushort length = registers[startIndex];
            
            int dataRegisterCount;
            if (lengthIsBytes)
            {
                // 长度是字节数，转换为寄存器数（向上取整）
                dataRegisterCount = (length + 1) / 2;
            }
            else
            {
                // 长度是寄存器数
                dataRegisterCount = length;
            }
            
            // 检查长度是否有效
            if (dataRegisterCount <= 0 || startIndex + 1 + dataRegisterCount > registers.Length)
                return string.Empty;
            
            // 读取字符串数据
            return GetString(registers, startIndex + 1, dataRegisterCount, encoding);
        }

        #endregion

        #region 写入字符串

        /// <summary>
        /// 将字符串写入寄存器（基本方法）
        /// </summary>
        /// <param name="value">要写入的字符串</param>
        /// <param name="encoding">字符串编码格式</param>
        /// <returns>寄存器数组</returns>
        private ushort[] SetString(string value, DataConvert.StringEncoding encoding = DataConvert.StringEncoding.ASCII)
        {
            if (string.IsNullOrEmpty(value))
                return new ushort[0];
            
            // 将字符串编码为字节数组
            byte[] bytes = EncodeStringToBytes(value, encoding);
            
            // 将字节数组转换为寄存器数组
            return BytesToRegisters(bytes, (bytes.Length + 1) / 2);
        }

        /// <summary>
        /// 将字符串写入固定长度的寄存器区域
        /// </summary>
        /// <param name="value">要写入的字符串</param>
        /// <param name="registerCount">固定寄存器数量</param>
        /// <param name="encoding">字符串编码格式</param>
        /// <param name="paddingChar">填充字符（默认为空格）</param>
        /// <returns>寄存器数组</returns>
        public ushort[] SetFixedLengthString(string value, int registerCount, 
            DataConvert.StringEncoding encoding = DataConvert.StringEncoding.ASCII, char paddingChar = ' ')
        {
            if (registerCount <= 0)
                throw new ArgumentException("寄存器数量必须大于0", nameof(registerCount));
            
            int maxBytes = registerCount * 2;
            
            // 编码字符串
            byte[] bytes = EncodeStringToBytes(value ?? string.Empty, encoding);
            
            // 如果字符串太长，截断
            if (bytes.Length > maxBytes)
            {
                // 根据编码截断，避免截断多字节字符
                bytes = TruncateBytes(bytes, maxBytes, encoding);
            }
            
            // 创建固定大小的字节数组
            byte[] fixedBytes = new byte[maxBytes];
            Array.Copy(bytes, fixedBytes, bytes.Length);
            
            // 填充剩余空间
            for (int i = bytes.Length; i < maxBytes; i++)
            {
                fixedBytes[i] = (byte)paddingChar;
            }
            
            // 转换为寄存器
            return BytesToRegisters(fixedBytes, registerCount);
        }

        /// <summary>
        /// 将字符串写入寄存器并以空字符结尾
        /// </summary>
        /// <param name="value">要写入的字符串</param>
        /// <param name="maxRegisterCount">最大寄存器数量</param>
        /// <param name="encoding">字符串编码格式</param>
        /// <returns>寄存器数组</returns>
        public ushort[] SetNullTerminatedString(string value, int maxRegisterCount, 
            DataConvert.StringEncoding encoding = DataConvert.StringEncoding.ASCII)
        {
            if (maxRegisterCount <= 0)
                throw new ArgumentException("最大寄存器数量必须大于0", nameof(maxRegisterCount));
            
            // 编码字符串
            byte[] bytes = EncodeStringToBytes(value ?? string.Empty, encoding);
            
            // 计算需要多少寄存器（字符串字节数 + 空字符）
            int totalBytes = bytes.Length + GetNullTerminatorSize(encoding);
            int requiredRegisters = (totalBytes + 1) / 2;
            
            if (requiredRegisters > maxRegisterCount)
            {
                // 如果超出最大长度，截断字符串
                int maxBytes = maxRegisterCount * 2 - GetNullTerminatorSize(encoding);
                bytes = TruncateBytes(bytes, maxBytes, encoding);
                totalBytes = bytes.Length + GetNullTerminatorSize(encoding);
                requiredRegisters = (totalBytes + 1) / 2;
            }
            
            // 创建带空字符的字节数组
            byte[] nullTerminatedBytes = new byte[requiredRegisters * 2];
            Array.Copy(bytes, nullTerminatedBytes, bytes.Length);
            
            // 添加空字符
            AddNullTerminator(nullTerminatedBytes, bytes.Length, encoding);
            
            // 转换为寄存器
            return BytesToRegisters(nullTerminatedBytes, requiredRegisters);
        }

        /// <summary>
        /// 将字符串写入寄存器并添加长度前缀
        /// </summary>
        /// <param name="value">要写入的字符串</param>
        /// <param name="encoding">字符串编码格式</param>
        /// <param name="lengthIsBytes">长度前缀是字节数还是寄存器数</param>
        /// <returns>寄存器数组</returns>
        public ushort[] SetLengthPrefixedString(string value, DataConvert.StringEncoding encoding = DataConvert.StringEncoding.ASCII, 
            bool lengthIsBytes = true)
        {
            // 编码字符串
            byte[] bytes = EncodeStringToBytes(value ?? string.Empty, encoding);
            
            // 计算数据寄存器数量
            int dataRegisterCount = (bytes.Length + 1) / 2;
            
            // 计算长度值
            ushort lengthValue = lengthIsBytes ? (ushort)bytes.Length : (ushort)dataRegisterCount;
            
            // 创建寄存器数组（长度寄存器 + 数据寄存器）
            ushort[] registers = new ushort[1 + dataRegisterCount];
            
            // 写入长度
            registers[0] = lengthValue;
            
            // 写入数据
            byte[] dataBytes = BytesToRegisterBytes(bytes, dataRegisterCount);
            for (int i = 0; i < dataRegisterCount; i++)
            {
                registers[1 + i] = (ushort)((dataBytes[i * 2] << 8) | dataBytes[i * 2 + 1]);
            }
            
            return registers;
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 将字节数组解码为字符串
        /// </summary>
        private string DecodeBytesToString(byte[] bytes, DataConvert.StringEncoding encoding)
        {
            switch (encoding)
            {
                case DataConvert.StringEncoding.ASCII:
                    return Encoding.ASCII.GetString(bytes);
                case DataConvert.StringEncoding.UTF8:
                    return Encoding.UTF8.GetString(bytes);
                case DataConvert.StringEncoding.UTF16LE:
                    return Encoding.Unicode.GetString(bytes);
                case DataConvert.StringEncoding.UTF16BE:
                    // UTF-16大端序需要转换
                    if (BitConverter.IsLittleEndian)
                    {
                        byte[] beBytes = new byte[bytes.Length];
                        for (int i = 0; i < bytes.Length; i += 2)
                        {
                            if (i + 1 < bytes.Length)
                            {
                                beBytes[i] = bytes[i + 1];
                                beBytes[i + 1] = bytes[i];
                            }
                        }
                        return Encoding.Unicode.GetString(beBytes);
                    }
                    return Encoding.Unicode.GetString(bytes);
                case DataConvert.StringEncoding.GB2312:
                    return Encoding.GetEncoding("GB2312").GetString(bytes);
                default:
                    return Encoding.ASCII.GetString(bytes);
            }
        }

        /// <summary>
        /// 将字符串编码为字节数组
        /// </summary>
        private byte[] EncodeStringToBytes(string value, DataConvert.StringEncoding encoding)
        {
            switch (encoding)
            {
                case DataConvert.StringEncoding.ASCII:
                    return Encoding.ASCII.GetBytes(value);
                case DataConvert.StringEncoding.UTF8:
                    return Encoding.UTF8.GetBytes(value);
                case DataConvert.StringEncoding.UTF16LE:
                    return Encoding.Unicode.GetBytes(value);
                case DataConvert.StringEncoding.UTF16BE:
                    byte[] leBytes = Encoding.Unicode.GetBytes(value);
                    if (BitConverter.IsLittleEndian)
                    {
                        // 转换为大端序
                        byte[] beBytes = new byte[leBytes.Length];
                        for (int i = 0; i < leBytes.Length; i += 2)
                        {
                            beBytes[i] = leBytes[i + 1];
                            beBytes[i + 1] = leBytes[i];
                        }
                        return beBytes;
                    }
                    return leBytes;
                case DataConvert.StringEncoding.GB2312:
                    return Encoding.GetEncoding("GB2312").GetBytes(value);
                default:
                    return Encoding.ASCII.GetBytes(value);
            }
        }

        /// <summary>
        /// 判断是否为空字符结束符
        /// </summary>
        private bool IsNullTerminator(byte highByte, byte lowByte, DataConvert.StringEncoding encoding)
        {
            switch (encoding)
            {
                case DataConvert.StringEncoding.ASCII:
                case DataConvert.StringEncoding.UTF8:
                case DataConvert.StringEncoding.GB2312:
                    return highByte == 0 && lowByte == 0;
                case DataConvert.StringEncoding.UTF16LE:
                case DataConvert.StringEncoding.UTF16BE:
                    return highByte == 0 && lowByte == 0;
                default:
                    return highByte == 0 && lowByte == 0;
            }
        }

        /// <summary>
        /// 获取空字符结束符的大小（字节数）
        /// </summary>
        private int GetNullTerminatorSize(DataConvert.StringEncoding encoding)
        {
            switch (encoding)
            {
                case DataConvert.StringEncoding.ASCII:
                case DataConvert.StringEncoding.UTF8:
                case DataConvert.StringEncoding.GB2312:
                    return 1;
                case DataConvert.StringEncoding.UTF16LE:
                case DataConvert.StringEncoding.UTF16BE:
                    return 2;
                default:
                    return 1;
            }
        }

        /// <summary>
        /// 添加空字符结束符
        /// </summary>
        private void AddNullTerminator(byte[] bytes, int startIndex, DataConvert.StringEncoding encoding)
        {
            switch (encoding)
        {
            case DataConvert.StringEncoding.ASCII:
            case DataConvert.StringEncoding.UTF8:
            case DataConvert.StringEncoding.GB2312:
                bytes[startIndex] = 0;
                break;
            case DataConvert.StringEncoding.UTF16LE:
            case DataConvert.StringEncoding.UTF16BE:
                bytes[startIndex] = 0;
                bytes[startIndex + 1] = 0;
                break;
        }
    }

        /// <summary>
        /// 截断字节数组，避免截断多字节字符
        /// </summary>
        private byte[] TruncateBytes(byte[] bytes, int maxBytes, DataConvert.StringEncoding encoding)
        {
            if (bytes.Length <= maxBytes)
                return bytes;
            
            if (encoding == DataConvert.StringEncoding.UTF8)
            {
                // UTF-8特殊处理，避免截断多字节字符
                int validLength = maxBytes;
                while (validLength > 0 && (bytes[validLength] & 0xC0) == 0x80)
                {
                    validLength--;
                }
                byte[] result = new byte[validLength];
                Array.Copy(bytes, result, validLength);
                return result;
            }
            else
            {
                byte[] result = new byte[maxBytes];
                Array.Copy(bytes, result, maxBytes);
                return result;
            }
        }

        /// <summary>
        /// 将字节数组转换为寄存器字节数组（用于写入）
        /// </summary>
        private byte[] BytesToRegisterBytes(byte[] bytes, int registerCount)
        {
            byte[] registerBytes = new byte[registerCount * 2];
            
            // 根据寄存器顺序和系统字节序调整
            byte[] orderedBytes = ApplyInverseRegisterOrder(bytes, registerCount,ModbusDataType);
            
            return orderedBytes;
        }

        #endregion

        protected interface IModbusClientAdapter
        {
            int ReadTimeout { get; set; }
            int WriteTimeout { get; set; }
            void Close();
            bool[] ReadCoils(byte stationId, int startAddress, ushort length);
            bool[] ReadDiscreteInputs(byte stationId, int startAddress, ushort length);
            ushort[] ReadHoldingRegisters(byte stationId, ushort startAddress, ushort length);
            ushort[] ReadInputRegisters(byte stationId, ushort startAddress, ushort length);
            void WriteSingleCoil(byte stationId, int address, bool value);
            void WriteSingleRegister(byte stationId, int address, short value);
            void WriteMultipleCoils(byte stationId, int startAddress, bool[] values);
            void WriteMultipleRegisters(byte stationId, int startAddress, int[] values);
            void WriteMultipleRegisters(byte stationId, int startAddress, short[] values);
        }

        protected sealed class FluentModbusRtuClientAdapter : IModbusClientAdapter
        {
            private ModbusRtuClient _client = new ModbusRtuClient();
            private readonly int _baudRate;
            private readonly int _dataBits;
            private readonly IoParity _parity;
            private readonly IoStopBits _stopBits;
            private string? _portName;

            public FluentModbusRtuClientAdapter(int baudRate, int dataBits, IoParity parity, IoStopBits stopBits)
            {
                _baudRate = baudRate;
                _dataBits = dataBits;
                _parity = parity;
                _stopBits = stopBits;
            }

            public int ReadTimeout
            {
                get => _client.ReadTimeout;
                set => _client.ReadTimeout = value;
            }

            public int WriteTimeout
            {
                get => _client.WriteTimeout;
                set => _client.WriteTimeout = value;
            }

            public bool IsOpen => _client.IsConnected;

            public bool Matches(string portName, int baudRate, int dataBits, IoParity parity, IoStopBits stopBits)
            {
                return string.Equals(_portName, portName, StringComparison.OrdinalIgnoreCase)
                       && _baudRate == baudRate
                       && _dataBits == dataBits
                       && _parity == parity
                       && _stopBits == stopBits;
            }

            public void Connect(string portName)
            {
                if (string.IsNullOrWhiteSpace(portName))
                    throw new ArgumentException("Serial port name is empty.", nameof(portName));

                portName = ControllerParameterParser.NormalizeSerialPortName(portName);

                if (IsOpen && Matches(portName, _baudRate, _dataBits, _parity, _stopBits))
                    return;

                Close();

                _client.BaudRate = _baudRate;
                _client.Parity = _parity;
                _client.StopBits = _stopBits;
                _client.Connect(portName, ModbusEndianness.LittleEndian);
                _portName = portName;
            }

            public void Close()
            {
                if (_client is IDisposable disposable)
                    disposable.Dispose();

                _client = new ModbusRtuClient();
                _portName = null;
            }

            public bool[] ReadCoils(byte stationId, int startAddress, ushort length) =>
                ToBoolArray(_client.ReadCoils(stationId, startAddress, length).ToArray(), length);

            public bool[] ReadDiscreteInputs(byte stationId, int startAddress, ushort length) =>
                ToBoolArray(_client.ReadDiscreteInputs(stationId, startAddress, length).ToArray(), length);

            public ushort[] ReadHoldingRegisters(byte stationId, ushort startAddress, ushort length) =>
                ToUInt16Array(_client.ReadHoldingRegisters(stationId, startAddress, length).ToArray(), length);

            public ushort[] ReadInputRegisters(byte stationId, ushort startAddress, ushort length) =>
                ToUInt16Array(_client.ReadInputRegisters(stationId, startAddress, length).ToArray(), length);

            public void WriteSingleCoil(byte stationId, int address, bool value) =>
                _client.WriteSingleCoil(stationId, address, value);

            public void WriteSingleRegister(byte stationId, int address, short value) =>
                _client.WriteSingleRegister(stationId, address, value);

            public void WriteMultipleCoils(byte stationId, int startAddress, bool[] values) =>
                _client.WriteMultipleCoils(stationId, startAddress, values);

            public void WriteMultipleRegisters(byte stationId, int startAddress, int[] values) =>
                _client.WriteMultipleRegisters(stationId, startAddress, values);

            public void WriteMultipleRegisters(byte stationId, int startAddress, short[] values) =>
                _client.WriteMultipleRegisters(stationId, startAddress, values);

            private static bool[] ToBoolArray(byte[] bytes, ushort length)
            {
                var values = new bool[length];
                for (var i = 0; i < length; i++)
                    values[i] = i < bytes.Length && bytes[i] != 0;
                return values;
            }

            private static ushort[] ToUInt16Array(byte[] bytes, ushort length)
            {
                var values = new ushort[length];
                for (var i = 0; i < length; i++)
                {
                    var offset = i * 2;
                    if (offset + 1 < bytes.Length)
                        values[i] = (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
                }
                return values;
            }
        }
    }
}
