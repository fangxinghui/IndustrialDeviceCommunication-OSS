using System;
using CoreShared;
using CoreShared.Events;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Attributes;
using DeviceCommunication.Models;
using IConfiguration = DeviceCommunication.Abstractions.IConfiguration;

namespace DeviceCommunication.Protocols.IOCard.ArtTechnology
{
    /// <summary>
    /// 阿尔泰 USB3145A：8 路同步电流 AI（±20mA）。
    /// Connect 后启动 ART-DAQ 硬件连续采样；支持最新值读取与环形缓冲导出（IAnalogBurstSource）。
    /// Parameters：ArtDAQ://Dev1 或 ArtDAQ://Dev1;SampleRate=10000（默认 10 kHz）。
    /// </summary>
    [ControllerDriver("ART Technology", "USB3145A", ControllerType.DataAcquisition)]
    [ControllerDriver("Altai", "USB3145A", ControllerType.DataAcquisition)]
    [ControllerDriver("北京阿尔泰科技", "USB3145A", ControllerType.DataAcquisition)]
    // 兼容旧配置：此前挂在模拟量（AnalogModule）下
    [ControllerDriver("ART Technology", "USB3145A", ControllerType.AnalogModule)]
    [ControllerDriver("Altai", "USB3145A", ControllerType.AnalogModule)]
    [ControllerDriver("北京阿尔泰科技", "USB3145A", ControllerType.AnalogModule)]
    internal sealed class Usb3145ADevice : IController, IAnalogIo, IAnalogBurstSource
    {
        private readonly ControllerConfiguration _configuration;
        private ArtDaqInterop? _daq;
        private bool _disposed;

        public event EventHandler<ExceptionEvent>? OnException;
        public event EventHandler<ExecuteLogEvent>? OnExecuteLog;

        public bool IsConnected => _daq?.Initialized == true && _daq.ContinuousRunning;

        public bool IsBurstAvailable => IsConnected;
        public double SampleRateHz => _daq?.SampleRateHz ?? 0;
        public int BurstChannelCount => ArtDaqInterop.ChannelCount;

        public Usb3145ADevice(ControllerConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public IConfiguration? CreateConfiguration() => null;

        public bool Initialize<T>(T parameter) => Connect(0);

        public bool Connect(int timeout)
        {
            try
            {
                Disconnect();

                _daq = new ArtDaqInterop();
                if (!_daq.Initialize(_configuration.Parameters))
                {
                    WriteLog(nameof(Connect), "Failed to initialize USB3145A / ART-DAQ.");
                    return false;
                }

                _daq.StartContinuous();
                WriteLog(nameof(Connect),
                    $"USB3145A '{_daq.DeviceName}' continuous AI started at {_daq.SampleRateHz:0.##} Hz.");
                return true;
            }
            catch (Exception ex)
            {
                WriteException(nameof(Connect), ex);
                Disconnect();
                return false;
            }
        }

        public void Disconnect()
        {
            _daq?.Dispose();
            _daq = null;
        }

        public (bool success, object value) ReadValue(ControllerAddress? address)
        {
            if (!IsConnected || address is null)
                return (false, null!);

            try
            {
                if (!TryResolveChannel(address, out var channel))
                    return (false, null!);

                if (!_daq!.TryGetLatestAmps(channel, out var amps))
                    return (false, null!);

                return (true, amps * 1000.0); // mA
            }
            catch (Exception ex)
            {
                WriteException(nameof(ReadValue), ex);
                return (false, null!);
            }
        }

        public bool WriteValue<T>(ControllerAddress? address, T value) => false;

        public (bool success, object[] values) ReadValues<T>(ControllerAddress[]? addresses, ushort length)
        {
            if (addresses is null || addresses.Length == 0 || !IsConnected)
                return (false, Array.Empty<object>());

            try
            {
                var results = new object[length];
                for (var i = 0; i < length; i++)
                {
                    var addr = i < addresses.Length ? addresses[i] : addresses[^1];
                    var (success, val) = ReadValue(addr);
                    results[i] = success ? val : 0.0;
                }

                return (true, results);
            }
            catch (Exception ex)
            {
                WriteException(nameof(ReadValues), ex);
                return (false, Array.Empty<object>());
            }
        }

        public bool WriteValues<T>(ControllerAddress[]? addresses, T[]? values) => false;

        public bool ReadAnalogInput(ControllerAddress channel, out double value)
        {
            value = 0;
            var (success, val) = ReadValue(channel);
            if (!success)
                return false;

            value = Convert.ToDouble(val);
            return true;
        }

        public bool ReadAnalogInputReading(ControllerAddress channel, out AnalogInputReading reading)
        {
            reading = new AnalogInputReading();
            if (!ReadAnalogInput(channel, out var milliAmps))
                return false;

            reading = new AnalogInputReading
            {
                RawValue = CurrentMaToRaw(milliAmps),
                SignalValue = milliAmps,
                SignalUnit = "mA"
            };
            return true;
        }

        public bool ReadAllAnalogInputs(ControllerAddress[] startRegister, int count, out double[] values)
        {
            values = Array.Empty<double>();
            if (!IsConnected || count <= 0)
                return false;

            try
            {
                var start = 0;
                if (startRegister is { Length: > 0 })
                    TryResolveChannel(startRegister[0], out start);

                if (!_daq!.TryGetLatestAmps(start, count, out var amps))
                    return false;

                values = new double[amps.Length];
                for (var i = 0; i < amps.Length; i++)
                    values[i] = amps[i] * 1000.0;

                return true;
            }
            catch (Exception ex)
            {
                WriteException(nameof(ReadAllAnalogInputs), ex);
                return false;
            }
        }

        public bool TryCopyRecentSamples(out double[,] samplesByIndexThenChannel, out int sampleCount)
        {
            samplesByIndexThenChannel = new double[0, BurstChannelCount];
            sampleCount = 0;
            if (!IsBurstAvailable || _daq is null)
                return false;
            return _daq.TryCopyRecentSamples(out samplesByIndexThenChannel, out sampleCount);
        }

        public bool TryResolveBurstChannel(ControllerAddress address, out int channelIndex)
            => TryResolveChannel(address, out channelIndex);

        private static bool TryResolveChannel(ControllerAddress address, out int channel)
        {
            channel = -1;

            if (!string.IsNullOrWhiteSpace(address.Address))
            {
                var text = address.Address.Trim();

                // 兼容旧 Modbus 地址表：x=4;N → 通道 N（从 0 起）
                if (text.StartsWith("x=4;", StringComparison.OrdinalIgnoreCase))
                {
                    var body = text["x=4;".Length..];
                    var semi = body.IndexOf(';');
                    if (semi >= 0)
                        body = body[..semi];
                    if (int.TryParse(body.Trim(), out var reg) &&
                        reg >= 0 && reg < ArtDaqInterop.ChannelCount)
                    {
                        channel = reg;
                        return true;
                    }
                }

                if (text.StartsWith("ai", StringComparison.OrdinalIgnoreCase))
                    text = text[2..];

                var colon = text.IndexOf(':');
                if (colon >= 0)
                    text = text[..colon];

                if (int.TryParse(text, out var fromAddress) &&
                    fromAddress >= 0 && fromAddress < ArtDaqInterop.ChannelCount)
                {
                    channel = fromAddress;
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(address.Channel) &&
                int.TryParse(address.Channel.Trim(), out var fromChannel))
            {
                // 兼容 1 基通道号（1..8 → 0..7）
                if (fromChannel >= 1 && fromChannel <= ArtDaqInterop.ChannelCount)
                    fromChannel -= 1;

                if (fromChannel >= 0 && fromChannel < ArtDaqInterop.ChannelCount)
                {
                    channel = fromChannel;
                    return true;
                }
            }

            return false;
        }

        private static ushort CurrentMaToRaw(double milliAmps)
        {
            const double min = -20.0;
            const double max = 20.0;
            var clamped = Math.Max(min, Math.Min(max, milliAmps));
            var ratio = (clamped - min) / (max - min);
            return (ushort)Math.Max(0, Math.Min(ushort.MaxValue, (int)Math.Round(ratio * ushort.MaxValue)));
        }

        private void WriteLog(string method, string message)
        {
            OnExecuteLog?.Invoke(this, new ExecuteLogEvent
            {
                ClassName = nameof(Usb3145ADevice),
                MethodName = method,
                LogMessage = message
            });
        }

        private void WriteException(string method, Exception ex)
        {
            OnException?.Invoke(this, new ExceptionEvent
            {
                ClassName = nameof(Usb3145ADevice),
                MethodName = method,
                ErrorMessage = ex.Message
            });
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            Disconnect();
            _disposed = true;
        }
    }
}
