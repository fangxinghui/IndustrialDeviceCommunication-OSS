using System;
using System.Collections.Generic;
using System.Linq;
using CoreShared;
using CoreShared.Events;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Attributes;
using DeviceCommunication.Models;
using IConfiguration = DeviceCommunication.Abstractions.IConfiguration;

namespace DeviceCommunication.Protocols.IOCard.NationalInstruments
{
    /// <summary>
    /// NI 多功能 DAQ 卡驱动（如 PCIe-6363、USB-600x 等）。
    /// 一个设备实例同时提供 AI / AO / DI / DO 四类接口，
    /// ControllerClient 根据地址表中每条信号的 AddressType 自动路由到对应接口。
    ///
    /// 设备名称通过 ControllerConfiguration.Parameters 传入（如 "Dev1" 或 "NIDAQ://Dev1"），
    /// 为空时自动选择系统上第一个可用的 NI DAQ 设备。
    /// </summary>
    [ControllerDriver("NI", "*", ControllerType.DataAcquisition, IsDefault = true)]
    [ControllerDriver("National Instruments", "*", ControllerType.DataAcquisition)]
    internal sealed class NIDaqDevice : IController, IAnalogIo, IAnalogOutputIo, IDigitalIo
    {
        private readonly ControllerConfiguration _configuration;
        private NIDaqInterop? _daq;
        private bool _disposed;

        // DO 回读缓存：禁止对输出线 CreateDIChan（会改方向并触发 USB abort/-50405）
        private readonly Dictionary<string, bool> _doWriteCache =
            new(StringComparer.OrdinalIgnoreCase);

        // 轮询失败时避免 Error 日志刷屏拖垮 UI
        private string? _lastExceptionKey;
        private DateTime _lastExceptionUtc = DateTime.MinValue;
        private int _suppressedExceptionCount;
        private DateTime _ioCooldownUntilUtc = DateTime.MinValue;
        private static readonly TimeSpan ExceptionLogInterval = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan IoFaultCooldown = TimeSpan.FromSeconds(2);

        // ── 电压量程默认值（可根据需要扩展为可配置） ──
        private const double DefaultAiMin = -10.0;
        private const double DefaultAiMax = 10.0;
        private const double DefaultAoMin = -10.0;
        private const double DefaultAoMax = 10.0;

        public event EventHandler<ExceptionEvent>? OnException;
        public event EventHandler<ExecuteLogEvent>? OnExecuteLog;

        public bool IsConnected => _daq?.Initialized ?? false;

        public NIDaqDevice(ControllerConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        // ═══════════════════ IController ═══════════════════

        public IConfiguration? CreateConfiguration() => null;

        public bool Initialize<T>(T parameter) => Connect(0);

        public bool Connect(int timeout)
        {
            try
            {
                Disconnect();

                var deviceName = NormalizeNiDaqDeviceName(_configuration.Parameters);
                _daq = new NIDaqInterop();
                _daq.OnException += (_, e) => OnException?.Invoke(this, e);
                _daq.OnExecuteLog += (_, e) => OnExecuteLog?.Invoke(this, e);
                var ok = _daq.Initialize(deviceName);

                if (ok)
                {
                    WriteLog(nameof(Connect), $"NI DAQ device '{_daq.DeviceName}' initialized.");
                }
                else
                {
                    WriteLog(nameof(Connect), $"Failed to initialize NI DAQ device.");
                }

                return ok;
            }
            catch (Exception ex)
            {
                WriteException(nameof(Connect), ex);
                return false;
            }
        }

        public void Disconnect()
        {
            if (_daq != null)
            {
                WriteLog(nameof(Disconnect), $"Disconnecting NI DAQ device '{_daq.DeviceName}'.");
                _daq.Dispose();
                _daq = null;
            }

            _doWriteCache.Clear();
            _ioCooldownUntilUtc = DateTime.MinValue;
        }

        public (bool success, object value) ReadValue(ControllerAddress? address)
        {
            if (!IsConnected || address is null)
                return (false, null!);

            // USB abort 等故障冷却期内直接跳过，避免每轮轮询继续打驱动/刷日志
            if (DateTime.UtcNow < _ioCooldownUntilUtc)
                return (false, null!);

            try
            {
                switch (address.AddressType)
                {
                    case ControllerAddressType.AnalogInput:
                    {
                        var ch = ResolveChannel(address);
                        var (min, max, term) = ResolveAiOptions(address);
                        // 同通道/同量程/同接线方式会复用已配置的 Task，不必每次重建
                        _daq!.ConfigureAiChannel(ch, min, max, term);
                        var value = _daq.ReadAiSinglePoint();
                        return (true, value);
                    }

                    case ControllerAddressType.DigitalInput:
                    {
                        var line = ResolveLine(address);
                        var value = _daq!.ReadDiLine(line);
                        return (true, value);
                    }

                    case ControllerAddressType.DigitalOutput:
                    {
                        // 输出线禁止走 DI 读回：CreateDIChan 会改端口方向，USB 上易触发 -50405
                        var line = ResolveLine(address);
                        if (_doWriteCache.TryGetValue(line, out var cached))
                            return (true, cached);
                        return (true, false);
                    }

                    default:
                        return (false, null!);
                }
            }
            catch (Exception ex)
            {
                EnterIoCooldownIfNeeded(ex);
                WriteException(nameof(ReadValue), ex);
                return (false, null!);
            }
        }

        public bool WriteValue<T>(ControllerAddress? address, T value)
        {
            if (!IsConnected || address is null)
                return false;

            if (DateTime.UtcNow < _ioCooldownUntilUtc)
                return false;

            try
            {
                switch (address.AddressType)
                {
                    case ControllerAddressType.AnalogOutput:
                    {
                        var ch = ResolveChannel(address);
                        var doubleVal = Convert.ToDouble(value);
                        _daq!.ConfigureAoChannel(ch);
                        // WriteAoSinglePoint 内部写完即 ReleaseTask
                        return _daq.WriteAoSinglePoint(doubleVal);
                    }

                    case ControllerAddressType.DigitalOutput:
                    {
                        var line = ResolveLine(address);
                        var boolVal = Convert.ToBoolean(value);
                        var ok = _daq!.WriteDoLine(line, boolVal);
                        if (ok)
                            _doWriteCache[line] = boolVal;
                        return ok;
                    }

                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                EnterIoCooldownIfNeeded(ex);
                WriteException(nameof(WriteValue), ex);
                return false;
            }
        }

        public (bool success, object[] values) ReadValues<T>(ControllerAddress[]? addresses, ushort length)
        {
            if (addresses is null || addresses.Length == 0 || !IsConnected)
                return (false, Array.Empty<object>());

            try
            {
                var results = new object[length];
                for (int i = 0; i < length; i++)
                {
                    var addr = i < addresses.Length ? addresses[i] : addresses[^1];
                    var (success, val) = ReadValue(addr);
                    results[i] = success ? val : (typeof(T) == typeof(bool) ? false : 0.0);
                }
                return (true, results);
            }
            catch (Exception ex)
            {
                WriteException(nameof(ReadValues), ex);
                return (false, Array.Empty<object>());
            }
        }

        public bool WriteValues<T>(ControllerAddress[]? addresses, T[]? values)
        {
            if (addresses is null || values is null || !IsConnected)
                return false;

            try
            {
                for (int i = 0; i < values.Length; i++)
                {
                    var addr = i < addresses.Length ? addresses[i] : addresses[^1];
                    if (!WriteValue(addr, values[i]))
                        return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                WriteException(nameof(WriteValues), ex);
                return false;
            }
        }

        // ═══════════════════ IAnalogIo ═══════════════════

        public bool ReadAnalogInput(ControllerAddress channel, out double value)
        {
            value = 0;
            var (success, val) = ReadValue(channel);
            if (success)
                value = Convert.ToDouble(val);
            return success;
        }

        public bool ReadAnalogInputReading(ControllerAddress channel, out AnalogInputReading reading)
        {
            reading = new AnalogInputReading();

            if (!ReadAnalogInput(channel, out var voltage))
                return false;

            // NI-DAQmx 返回电压值，RawValue 按 16 位 ADC 换算（±10V → 0~65535）
            var raw = VoltageToRaw(voltage, DefaultAiMin, DefaultAiMax);

            reading = new AnalogInputReading
            {
                RawValue = raw,
                SignalValue = voltage,
                SignalUnit = "V"
            };
            return true;
        }

        public bool ReadAllAnalogInputs(ControllerAddress[] startRegister, int count, out double[] values)
        {
            values = Array.Empty<double>();
            if (!IsConnected) return false;

            try
            {
                // 如果第一条地址配置了通道范围（如 "ai0:3"），使用它
                var first = startRegister is { Length: > 0 } ? startRegister[0] : null;
                var ch = first != null ? ResolveChannel(first) : "ai0";
                var (min, max, term) = ResolveAiOptions(first);

                _daq!.ConfigureAiChannel(ch, min, max, term);
                var data = _daq.ReadAiWaveform(count);
                // 波形读会改采样时钟配置，读完释放，避免残留 Task 占住通道
                _daq.ReleaseTask();

                values = data;
                return data.Length > 0;
            }
            catch (Exception ex)
            {
                WriteException(nameof(ReadAllAnalogInputs), ex);
                return false;
            }
        }

        // ═══════════════════ IAnalogOutputIo ═══════════════════

        public bool WriteAnalogOutput(ControllerAddress channel, double signalValue)
        {
            return WriteValue(channel, signalValue);
        }

        public bool WriteAnalogOutputRaw(ControllerAddress channel, ushort rawValue)
        {
            // 将 16 位原始值 (0~65535) 换算为电压
            var voltage = RawToVoltage(rawValue, DefaultAoMin, DefaultAoMax);
            return WriteAnalogOutput(channel, voltage);
        }

        public bool TryBuildAnalogOutputRaw(ControllerAddress channel, double signalValue, out ushort rawValue)
        {
            rawValue = VoltageToRaw(signalValue, DefaultAoMin, DefaultAoMax);
            return rawValue >= 0;
        }

        // ═══════════════════ IDigitalIo ═══════════════════

        public bool ReadDigitalInput(ControllerAddress channel, out bool value)
        {
            value = false;
            var (success, val) = ReadValue(channel);
            if (success && val is bool b)
                value = b;
            return success;
        }

        public bool ReadAllDigitalInputs(ControllerAddress[] startRegister, int count, out bool[] values)
        {
            values = Array.Empty<bool>();
            if (!IsConnected) return false;

            try
            {
                var results = new bool[count];
                for (int i = 0; i < count; i++)
                {
                    var addr = i < startRegister.Length
                        ? startRegister[i]
                        : new ControllerAddress { Address = ResolveLineFromChannel(startRegister?[0], i) };
                    var (success, val) = ReadValue(addr);
                    results[i] = success && val is bool b && b;
                }
                values = results;
                return true;
            }
            catch (Exception ex)
            {
                WriteException(nameof(ReadAllDigitalInputs), ex);
                return false;
            }
        }

        public bool WriteDigitalOutput(ControllerAddress channel, bool value)
        {
            return WriteValue(channel, value);
        }

        public bool WriteAllDigitalOutputs(ControllerAddress[] startRegister, bool[] values)
        {
            if (!IsConnected) return false;

            try
            {
                for (int i = 0; i < values.Length; i++)
                {
                    var addr = i < startRegister.Length
                        ? startRegister[i]
                        : new ControllerAddress { Address = ResolveLineFromChannel(startRegister?[0], i) };
                    if (!WriteDigitalOutput(addr, values[i]))
                        return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                WriteException(nameof(WriteAllDigitalOutputs), ex);
                return false;
            }
        }

        // ═══════════════════ 地址解析 ═══════════════════

        /// <summary>
        /// NI-DAQmx 只要 "Dev1"，兼容 UI 写入的 "NIDAQ://Dev1" 及附加 ";key=value"。
        /// </summary>
        private static string NormalizeNiDaqDeviceName(string? parameters)
        {
            var text = (parameters ?? string.Empty).Trim();
            foreach (var prefix in new[] { "NIDAQ://", "NI-DAQ://", "NI://", "DAQ://" })
            {
                if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                text = text[prefix.Length..].Trim();
                break;
            }

            var semi = text.IndexOf(';');
            if (semi >= 0)
                text = text[..semi].Trim();

            return text;
        }

        /// <summary>
        /// 从地址参数解析 AI 量程与接线方式。
        /// 例：ai4;term=RSE;sensorSignal=0-10V;physical=0-100kPa
        /// </summary>
        private static (double min, double max, int terminalConfig) ResolveAiOptions(ControllerAddress? address)
        {
            var min = DefaultAiMin;
            var max = DefaultAiMax;
            var term = NIDaqNative.ValCfgDefault;

            if (address is null || string.IsNullOrWhiteSpace(address.Address))
                return (min, max, term);

            var parameters = ParseAddressParameters(address.Address);

            if (parameters.TryGetValue("term", out var termText) ||
                parameters.TryGetValue("terminal", out termText) ||
                parameters.TryGetValue("terminalConfig", out termText))
            {
                term = ParseTerminalConfig(termText);
            }

            if (parameters.TryGetValue("range", out var rangeText) ||
                parameters.TryGetValue("inputRange", out rangeText))
            {
                if (TryParseRange(rangeText, out var lo, out var hi))
                {
                    min = lo;
                    max = hi;
                }
            }

            return (min, max, term);
        }

        private static int ParseTerminalConfig(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return NIDaqNative.ValCfgDefault;

            text = text.Trim();
            if (int.TryParse(text, out var numeric))
                return numeric;

            return text.ToUpperInvariant() switch
            {
                "DEFAULT" or "CFGDEFAULT" or "AUTO" => NIDaqNative.ValCfgDefault,
                "RSE" or "SINGLE" or "SE" => NIDaqNative.ValRse,
                "NRSE" => NIDaqNative.ValNrse,
                "DIFF" or "DIFFERENTIAL" => NIDaqNative.ValDiff,
                _ => NIDaqNative.ValCfgDefault
            };
        }

        private static bool TryParseRange(string? text, out double min, out double max)
        {
            min = DefaultAiMin;
            max = DefaultAiMax;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // 支持 "±10" / "+-10" / "10"
            var trimmed = text.Trim().TrimEnd('V', 'v');
            if (trimmed.StartsWith("±", StringComparison.Ordinal) ||
                trimmed.StartsWith("+-", StringComparison.Ordinal))
            {
                var numText = trimmed.TrimStart('±', '+', '-');
                if (double.TryParse(numText, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var span) && span > 0)
                {
                    min = -span;
                    max = span;
                    return true;
                }
            }

            var parts = trimmed.Split(new[] { '-', '~', '～' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 &&
                double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out min) &&
                double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out max))
            {
                return true;
            }

            if (double.TryParse(trimmed, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var abs) && abs > 0)
            {
                min = -abs;
                max = abs;
                return true;
            }

            return false;
        }

        private static Dictionary<string, string> ParseAddressParameters(string? address)
        {
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(address))
                return parameters;

            foreach (var part in address.Split(';'))
            {
                var separator = part.IndexOf('=');
                if (separator <= 0)
                    continue;

                var key = part[..separator].Trim();
                var value = part[(separator + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(key))
                    parameters[key] = value;
            }

            return parameters;
        }

        /// <summary>
        /// 从 ControllerAddress 解析 NI 物理通道名（如 "Dev1/ai0"）。
        /// 优先使用 Address 字段，其次组合 Channel。
        /// </summary>
        private string ResolveChannel(ControllerAddress address)
        {
            if (!string.IsNullOrWhiteSpace(address.Address))
            {
                // 地址中可能直接包含 "ai0"、"ai0:3"，后面可跟 ;sensorSignal=...;physical=...
                var ch = address.Address.Split(';')[0].Trim();
                if (ch.StartsWith("ai", StringComparison.OrdinalIgnoreCase) ||
                    ch.StartsWith("ao", StringComparison.OrdinalIgnoreCase))
                    return $"{_daq!.DeviceName}/{ch}";
            }

            if (!string.IsNullOrWhiteSpace(address.Channel))
            {
                var prefix = address.AddressType == ControllerAddressType.AnalogOutput ? "ao" : "ai";
                return $"{_daq!.DeviceName}/{prefix}{address.Channel}";
            }

            return $"{_daq!.DeviceName}/ai0";
        }

        /// <summary>
        /// 从 ControllerAddress 解析 NI 数字线名（如 "Dev1/port0/line0"）。
        /// 格式：Address="port0/line0" 或 Channel="0", Point="0"
        /// </summary>
        private string ResolveLine(ControllerAddress address)
        {
            if (!string.IsNullOrWhiteSpace(address.Address))
            {
                var line = address.Address.Split(';')[0].Trim();
                if (line.Contains("port", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("line", StringComparison.OrdinalIgnoreCase))
                    return $"{_daq!.DeviceName}/{line}";
            }

            var port = !string.IsNullOrWhiteSpace(address.Channel) ? address.Channel : "0";
            var lineNum = !string.IsNullOrWhiteSpace(address.Point) ? address.Point : "0";
            return $"{_daq!.DeviceName}/port{port}/line{lineNum}";
        }

        /// <summary>
        /// 根据起始地址和偏移量生成线名。
        /// </summary>
        private string ResolveLineFromChannel(ControllerAddress? startAddr, int offset)
        {
            if (startAddr is null)
                return $"{_daq!.DeviceName}/port0/line{offset}";

            if (!string.IsNullOrWhiteSpace(startAddr.Address))
            {
                var parts = startAddr.Address.Split(';')[0].Split('/');
                if (parts.Length >= 2 && parts[0].StartsWith("port", StringComparison.OrdinalIgnoreCase))
                {
                    var port = parts[0].Replace("port", "").Trim();
                    var lineBase = parts.Length > 1 ? parts[1].Replace("line", "").Trim() : "0";
                    if (int.TryParse(lineBase, out var baseLine))
                        return $"{_daq!.DeviceName}/port{port}/line{baseLine + offset}";
                }
            }

            return $"{_daq!.DeviceName}/port0/line{offset}";
        }

        // ═══════════════════ 工具方法 ═══════════════════

        /// <summary>将电压值换算为 16 位 ADC 原始值 (0~65535)</summary>
        private static ushort VoltageToRaw(double voltage, double min, double max)
        {
            if (Math.Abs(max - min) < 1e-9) return 0;
            var clamped = Math.Max(min, Math.Min(max, voltage));
            var ratio = (clamped - min) / (max - min);
            var raw = (int)Math.Round(ratio * ushort.MaxValue);
            return (ushort)Math.Max(0, Math.Min(ushort.MaxValue, raw));
        }

        /// <summary>将 16 位原始值 (0~65535) 换算为电压</summary>
        private static double RawToVoltage(ushort raw, double min, double max)
        {
            return min + (raw / (double)ushort.MaxValue) * (max - min);
        }

        private void WriteLog(string method, string message)
        {
            OnExecuteLog?.Invoke(this, new ExecuteLogEvent
            {
                ClassName = nameof(NIDaqDevice),
                MethodName = method,
                LogMessage = message
            });
        }

        private void EnterIoCooldownIfNeeded(Exception ex)
        {
            var msg = ex.Message ?? string.Empty;
            if (msg.Contains("-50405", StringComparison.Ordinal) ||
                msg.Contains("-50103", StringComparison.Ordinal))
            {
                _ioCooldownUntilUtc = DateTime.UtcNow + IoFaultCooldown;
            }
        }

        private void WriteException(string method, Exception ex)
        {
            // 同源错误限流：轮询周期内同一错误只打一条，避免日志/UI 被打满
            var key = $"{method}|{NormalizeExceptionKey(ex.Message)}";
            var now = DateTime.UtcNow;
            if (string.Equals(key, _lastExceptionKey, StringComparison.Ordinal) &&
                now - _lastExceptionUtc < ExceptionLogInterval)
            {
                _suppressedExceptionCount++;
                return;
            }

            var suppressed = _suppressedExceptionCount;
            _lastExceptionKey = key;
            _lastExceptionUtc = now;
            _suppressedExceptionCount = 0;

            var message = suppressed > 0
                ? $"{ex.Message} (suppressed {suppressed} similar errors in last {ExceptionLogInterval.TotalSeconds:0}s)"
                : ex.Message;

            OnException?.Invoke(this, new ExceptionEvent
            {
                ClassName = nameof(NIDaqDevice),
                MethodName = method,
                ErrorMessage = message
            });
        }

        /// <summary>去掉 Task Name 等每次变化的字段，保证限流能命中。</summary>
        private static string NormalizeExceptionKey(string? message)
        {
            if (string.IsNullOrEmpty(message))
                return string.Empty;

            // "DAQmxStartTask failed (-50405): ..." → 用错误码做主键
            var start = message.IndexOf("(-", StringComparison.Ordinal);
            var end = start >= 0 ? message.IndexOf(')', start) : -1;
            if (start >= 0 && end > start)
                return message[(start + 1)..end]; // e.g. -50405

            return message.Length <= 120 ? message : message[..120];
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Disconnect();
                _disposed = true;
            }
        }
    }
}
