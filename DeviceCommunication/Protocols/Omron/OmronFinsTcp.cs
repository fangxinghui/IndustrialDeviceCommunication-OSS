using CoreShared;
using CoreShared.Events;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Models;
using HslCommunication;
using HslCommunication.Core;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace DeviceCommunication.Protocols.Omron
{
    internal class OmronFinsTcp : IController
    {
        /// <summary>
        /// Omron 地址：D/C/W/H/A + 字地址，可选 .位号（如 D100、C0.0、D100.15）
        /// </summary>
        private static readonly Regex OmronAddressRegex = new(
            @"^(?<area>[DCHWA])(?<word>\d+)(?:\.(?<bit>\d+))?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public bool IsConnected => PLCConnected;
        public event EventHandler<ExceptionEvent>? OnException;
        public event EventHandler<ExecuteLogEvent>? OnExecuteLog;
        private HslCommunication.Profinet.Omron.OmronFinsNet? _omronFinsNet;
        private bool PLCConnected = false;
        private OmronNetConfiguration? omronNetConfiguration;
        private int _connectTimeout = 5000;
        public IConfiguration? CreateConfiguration() => omronNetConfiguration;
        public bool Connect(int timeout)
        {
            try
            {
                WriteLog(this, new ExecuteLogEvent() { LogMessage = "Start connect omron plc", ClassName = nameof(OmronFinsTcp), MethodName = nameof(Connect) });
                if (_omronFinsNet is null)
                {
                    WriteLog(this, new ExecuteLogEvent() { LogMessage = "OmronFinsNet is not initialized.", ClassName = nameof(OmronFinsTcp), MethodName = nameof(Connect) });
                    return false;
                }
                _connectTimeout = timeout;
                _omronFinsNet.ConnectTimeOut = _connectTimeout;
                HslCommunication.OperateResult result = _omronFinsNet.ConnectServer();
                PLCConnected = result.IsSuccess;
                WriteLog(this, new ExecuteLogEvent() { LogMessage = $"Connect omron plc result: {result.IsSuccess}, message: {result.Message}", ClassName = nameof(OmronFinsTcp), MethodName = nameof(Connect) });
            }
            catch (Exception ex)
            {
                WriteException(this, new ExceptionEvent { ErrorMessage = ex.Message, ClassName = nameof(OmronFinsTcp), MethodName = nameof(Connect) });
            }
            return PLCConnected;
        }



        public void Disconnect()
        {
            try
            {
                WriteLog(this, new ExecuteLogEvent() { LogMessage = "disconnect omron plc", ClassName = nameof(OmronFinsTcp), MethodName = nameof(Disconnect) });
                _omronFinsNet?.ConnectClose();
                PLCConnected = false;
            }
            catch (Exception ex)
            {
                WriteException(this, new ExceptionEvent { ErrorMessage = ex.Message, ClassName = nameof(OmronFinsTcp), MethodName = nameof(Disconnect) });
            }
        }

        public bool Initialize<T>(T parameter)
        {
            if (parameter is OmronNetConfiguration config)
            {
                if (string.IsNullOrEmpty(config.IP))
                {
                    WriteLog(this, new ExecuteLogEvent { LogMessage = "IP is null or empty.", ClassName = nameof(OmronFinsTcp), MethodName = nameof(Initialize) });
                    return false;
                }
                omronNetConfiguration = config;
                _omronFinsNet = new HslCommunication.Profinet.Omron.OmronFinsNet();
                _omronFinsNet.IpAddress = config.IP;
                _omronFinsNet.Port = config.Port;
                _omronFinsNet.SA1 = config.ComputerSA1;
                _omronFinsNet.DA2 = config.PLCDA2;
                _omronFinsNet.ByteTransform.DataFormat = config.DataFormat switch
                {
                    ModbusDataType.ABCD => HslCommunication.Core.DataFormat.ABCD,
                    ModbusDataType.BADC => HslCommunication.Core.DataFormat.BADC,
                    ModbusDataType.CDAB => HslCommunication.Core.DataFormat.CDAB,
                    ModbusDataType.DCBA => HslCommunication.Core.DataFormat.DCBA,
                    _ => HslCommunication.Core.DataFormat.ABCD
                };
                WriteLog(this, new ExecuteLogEvent { LogMessage = "OmronFinsTcp initialized successfully.", ClassName = nameof(OmronFinsTcp), MethodName = nameof(Initialize) });
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
                        operateResult = _omronFinsNet?.ReadBool(address.Address) ?? new OperateResult<bool>() { IsSuccess = false };
                        return (operateResult.IsSuccess, operateResult.Content);
                    case DataConvert.DataType.UInt16:
                        operateResult = _omronFinsNet?.ReadUInt16(address.Address) ?? new OperateResult<ushort>() { IsSuccess = false };
                        return (operateResult.IsSuccess, operateResult.Content);
                    case DataConvert.DataType.Int16:
                        operateResult = _omronFinsNet?.ReadInt16(address.Address) ?? new OperateResult<short>() { IsSuccess = false };
                        return (operateResult.IsSuccess, operateResult.Content);
                    case DataConvert.DataType.UInt32:
                        operateResult = _omronFinsNet?.ReadUInt32(address.Address) ?? new OperateResult<uint>() { IsSuccess = false };
                        return (operateResult.IsSuccess, operateResult.Content);
                    case DataConvert.DataType.Int32:
                        operateResult = _omronFinsNet?.ReadInt32(address.Address) ?? new OperateResult<int>() { IsSuccess = false };
                        return (operateResult.IsSuccess, operateResult.Content);
                    case DataConvert.DataType.Float:
                        operateResult = _omronFinsNet?.ReadFloat(address.Address) ?? new OperateResult<float>() { IsSuccess = false };
                        return (operateResult.IsSuccess, operateResult.Content);
                    case DataConvert.DataType.Double:
                        operateResult = _omronFinsNet?.ReadDouble(address.Address) ?? new OperateResult<double>() { IsSuccess = false };
                        return (operateResult.IsSuccess, operateResult.Content);
                    case DataConvert.DataType.ULong:
                        operateResult = _omronFinsNet?.ReadUInt64(address.Address) ?? new OperateResult<ulong>() { IsSuccess = false };
                        return (operateResult.IsSuccess, operateResult.Content);
                    case DataConvert.DataType.Long:
                        operateResult = _omronFinsNet?.ReadInt64(address.Address) ?? new OperateResult<long>() { IsSuccess = false };
                        return (operateResult.IsSuccess, operateResult.Content);
                    case DataConvert.DataType.String:
                        operateResult = _omronFinsNet?.ReadString(address.Address, (ushort)address.Length) ?? new OperateResult<string>() { IsSuccess = false };
                        return (operateResult.IsSuccess, operateResult.Content);
                    default:
                        throw new NotImplementedException();
                }
            }
            catch (Exception ex)
            {
                WriteException(this, new ExceptionEvent { ErrorMessage = ex.Message, ClassName = nameof(OmronFinsTcp), MethodName = nameof(ReadValue) });
                return (false, Array.Empty<object>());
            }
        }

        /// <summary>Omron FINS 字区：1 字 = 2 字节。</summary>
        private const int BytesPerWord = 2;

        /// <summary>
        /// 连续批量读：从地址列表取最小地址为起点，一次 Read 读取 <paramref name="length"/> 个字，再按 DataType 字节宽度解析。
        /// <para>
        /// <paramref name="length"/> 语义（与轮询 max-min+1 一致）：
        /// Bool = 位数；其它类型 = <b>字数</b>（不是元素个数）。
        /// </para>
        /// 各类型占用（须整除，否则抛错）：
        /// Byte=1B；Short/UInt16/Int16=2B=1字；Int/UInt32/Float=4B=2字；Long/ULong/Double=8B=4字。
        /// </summary>
        public (bool success, object[] values) ReadValues<T>(ControllerAddress[]? addresses, ushort length)
        {
            try
            {
                if (_omronFinsNet is null)
                    throw new InvalidOperationException("OmronFinsNet is not initialized.");
                if (addresses is null || addresses.Length == 0)
                    throw new ArgumentException("地址列表不能为空", nameof(addresses));
                if (length == 0)
                    throw new ArgumentException("读取长度不能为 0", nameof(length));

                var start = ResolveContinuousReadStart(addresses);
                if (!start.IsValid)
                    throw new ArgumentException(start.ErrorMessage);

                if (start.DataType == DataConvert.DataType.Bool)
                {
                    // length = 连续位数
                    OperateResult<bool[]> boolResult = _omronFinsNet.ReadBool(start.StartAddress, length);
                    if (!boolResult.IsSuccess)
                        throw new Exception($"批量读取 Bool 失败: {boolResult.Message}");
                    WriteLog(this, new ExecuteLogEvent
                    {
                        LogMessage = $"ReadValues Bool '{start.StartAddress}' bits={length} success",
                        ClassName = nameof(OmronFinsTcp),
                        MethodName = nameof(ReadValues)
                    });
                    return (true, boolResult.Content.Cast<object>().ToArray());
                }

                if (start.DataType == DataConvert.DataType.String)
                    throw new NotSupportedException("批量读取不支持 String，请使用 ReadValue");

                // length = 字数；再按类型字节数换算可解析的元素个数
                int elementCount = GetElementCountFromWordLength(start.DataType, length);
                int requiredBytes = checked(elementCount * GetElementSizeInBytes(start.DataType));

                OperateResult<byte[]> readResult = _omronFinsNet.Read(start.StartAddress, length);
                if (!readResult.IsSuccess || readResult.Content is null)
                    throw new Exception($"批量读取失败: {readResult.Message}");
                if (readResult.Content.Length < requiredBytes)
                    throw new Exception($"返回字节不足: 需要 {requiredBytes} 字节（{length} 字 × 类型 {start.DataType}），实际 {readResult.Content.Length}");

                object[] values = ParseBuffer(readResult.Content, start.DataType, elementCount);
                WriteLog(this, new ExecuteLogEvent
                {
                    LogMessage = $"ReadValues '{start.StartAddress}' type={start.DataType} words={length} bytes/element={GetElementSizeInBytes(start.DataType)} elements={elementCount} success",
                    ClassName = nameof(OmronFinsTcp),
                    MethodName = nameof(ReadValues)
                });
                return (true, values);
            }
            catch (Exception ex)
            {
                WriteException(this, new ExceptionEvent { ErrorMessage = ex.Message, ClassName = nameof(OmronFinsTcp), MethodName = nameof(ReadValues) });
                return (false, Array.Empty<object>());
            }
        }

        /// <summary>
        /// 校验同区、同类型，并取最小字/位地址作为连续读起点。
        /// </summary>
        private static (bool IsValid, string StartAddress, DataConvert.DataType DataType, string ErrorMessage) ResolveContinuousReadStart(
            ControllerAddress[] addresses)
        {
            string? area = null;
            DataConvert.DataType? dataType = null;
            int minWord = int.MaxValue;
            int minBit = int.MaxValue;
            string? startAddress = null;

            foreach (var address in addresses)
            {
                if (address is null || string.IsNullOrWhiteSpace(address.Address))
                    return (false, string.Empty, DataConvert.DataType.Bool, "地址不能为空");

                if (!TryParseOmronAddress(address.Address, out string parsedArea, out int word, out int bit))
                    return (false, string.Empty, DataConvert.DataType.Bool, $"无法解析 Omron 地址 '{address.Address}'");

                if (area is null)
                    area = parsedArea;
                else if (!area.Equals(parsedArea, StringComparison.OrdinalIgnoreCase))
                    return (false, string.Empty, DataConvert.DataType.Bool, $"批量读取要求同一软元件区，发现 {area} 与 {parsedArea}");

                if (dataType is null)
                    dataType = address.DataType;
                else if (dataType.Value != address.DataType)
                    return (false, string.Empty, DataConvert.DataType.Bool, $"批量读取要求同一数据类型，发现 {dataType} 与 {address.DataType}");

                if (word < minWord || (word == minWord && bit < minBit))
                {
                    minWord = word;
                    minBit = bit;
                    startAddress = address.Address;
                }
            }

            if (startAddress is null || dataType is null)
                return (false, string.Empty, DataConvert.DataType.Bool, "地址排序失败");

            return (true, startAddress, dataType.Value, string.Empty);
        }

        private static bool TryParseOmronAddress(string address, out string area, out int word, out int bit)
        {
            area = string.Empty;
            word = 0;
            bit = 0;
            var match = OmronAddressRegex.Match(address.Trim());
            if (!match.Success)
                return false;
            area = match.Groups["area"].Value.ToUpperInvariant();
            if (!int.TryParse(match.Groups["word"].Value, out word))
                return false;
            if (match.Groups["bit"].Success)
            {
                if (!int.TryParse(match.Groups["bit"].Value, out bit))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 单个元素占用字节数（Omron 字区按此宽度切分缓冲）。
        /// </summary>
        private static int GetElementSizeInBytes(DataConvert.DataType dataType) => dataType switch
        {
            DataConvert.DataType.Bool => 1, // 位读不走此路径；占位
            DataConvert.DataType.Byte => 1,
            DataConvert.DataType.UInt16 or DataConvert.DataType.Int16 => 2,   // short = 1 字
            DataConvert.DataType.UInt32 or DataConvert.DataType.Int32 or DataConvert.DataType.Float => 4, // int/float = 2 字
            DataConvert.DataType.ULong or DataConvert.DataType.Long or DataConvert.DataType.Double => 8,   // long/double = 4 字
            DataConvert.DataType.String => throw new NotSupportedException("String 无固定字宽，不能按连续字缓冲解析"),
            _ => throw new NotImplementedException($"不支持的数据类型 {dataType}")
        };

        /// <summary>
        /// 由读取字数换算可完整解析的元素个数。总字节 = 字数×2，必须能被元素字节宽度整除。
        /// 例：length=4 字=8 字节 → short/Int16 得 4 个；int/Float 得 2 个；long 得 1 个。
        /// </summary>
        private static int GetElementCountFromWordLength(DataConvert.DataType dataType, ushort wordLength)
        {
            int totalBytes = checked(wordLength * BytesPerWord);
            int sizeInBytes = GetElementSizeInBytes(dataType);

            if (dataType == DataConvert.DataType.Byte)
                return totalBytes; // 每字 2 字节 → 2 个 Byte

            if (totalBytes % sizeInBytes != 0)
            {
                int wordsPerElement = sizeInBytes / BytesPerWord;
                throw new ArgumentException(
                    $"字长度 {wordLength}（{totalBytes} 字节）无法按类型 {dataType}（每元素 {sizeInBytes} 字节 = {wordsPerElement} 字）完整切分，请保证 length 能被 {wordsPerElement} 整除");
            }

            int count = totalBytes / sizeInBytes;
            if (count <= 0 || count > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(wordLength), "计算出的元素个数超出范围");
            return count;
        }

        private object[] ParseBuffer(byte[] buffer, DataConvert.DataType dataType, int count)
        {
            IByteTransform bt = _omronFinsNet!.ByteTransform;
            return dataType switch
            {
                DataConvert.DataType.Byte => Enumerable.Range(0, count).Select(i => (object)bt.TransByte(buffer, i)).ToArray(),
                DataConvert.DataType.UInt16 => bt.TransUInt16(buffer, 0, count).Cast<object>().ToArray(),
                DataConvert.DataType.Int16 => bt.TransInt16(buffer, 0, count).Cast<object>().ToArray(),
                DataConvert.DataType.UInt32 => bt.TransUInt32(buffer, 0, count).Cast<object>().ToArray(),
                DataConvert.DataType.Int32 => bt.TransInt32(buffer, 0, count).Cast<object>().ToArray(),
                DataConvert.DataType.Float => bt.TransSingle(buffer, 0, count).Cast<object>().ToArray(),
                DataConvert.DataType.ULong => bt.TransUInt64(buffer, 0, count).Cast<object>().ToArray(),
                DataConvert.DataType.Long => bt.TransInt64(buffer, 0, count).Cast<object>().ToArray(),
                DataConvert.DataType.Double => bt.TransDouble(buffer, 0, count).Cast<object>().ToArray(),
                _ => throw new NotImplementedException($"不支持的批量数据类型 {dataType}")
            };
        }

        public bool WriteValue<T>(ControllerAddress? address, T value)
        {
            try
            {
                if (address is null || string.IsNullOrEmpty(address.Address))
                    throw new ArgumentException("地址不能为空", nameof(address));
                OperateResult operateResult = typeof(T) switch
                {
                    Type t when t == typeof(bool) => _omronFinsNet?.Write(address.Address, Convert.ToBoolean(value)),
                    //Type t when t == typeof(byte) => _omronFinsNet?.Write(address.Address, Convert.ToByte(value)),
                    Type t when t == typeof(ushort) => _omronFinsNet?.Write(address.Address, Convert.ToUInt16(value)),
                    Type t when t == typeof(short) => _omronFinsNet?.Write(address.Address, Convert.ToInt16(value)),
                    Type t when t == typeof(uint) => _omronFinsNet?.Write(address.Address, Convert.ToUInt32(value)),
                    Type t when t == typeof(int) => _omronFinsNet?.Write(address.Address, Convert.ToInt32(value)),
                    Type t when t == typeof(float) => _omronFinsNet?.Write(address.Address, Convert.ToSingle(value)),
                    Type t when t == typeof(double) => _omronFinsNet?.Write(address.Address, Convert.ToDouble(value)),
                    Type t when t == typeof(ulong) => _omronFinsNet?.Write(address.Address, Convert.ToUInt64(value)),
                    Type t when t == typeof(long) => _omronFinsNet?.Write(address.Address, Convert.ToInt64(value)),
                    Type t when t == typeof(string) => address.Length > 0
                        ? _omronFinsNet?.Write(address.Address, Convert.ToString(value) ?? string.Empty, address.Length)
                        : _omronFinsNet?.Write(address.Address, Convert.ToString(value) ?? string.Empty),
                    _ => throw new NotImplementedException($"不支持的数据类型 {typeof(T)}")
                };
                return operateResult?.IsSuccess ?? false;
            }
            catch (Exception ex)
            {
                WriteException(this, new ExceptionEvent { ErrorMessage = ex.Message, ClassName = nameof(OmronFinsTcp), MethodName = nameof(WriteValue) });
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
                WriteException(this, new ExceptionEvent { ErrorMessage = ex.Message, ClassName = nameof(OmronFinsTcp), MethodName = nameof(WriteValues) });
                return false;
            }
        }
        protected void WriteLog(object sender, ExecuteLogEvent e)
        {
            OnExecuteLog?.Invoke(sender, e);
        }
        protected void WriteException(object sender, ExceptionEvent e)
        {
            OnException?.Invoke(sender, e);
        }
    }
}
