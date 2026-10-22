using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

namespace DeviceCommunication.Protocols.IOCard.ArtTechnology
{
    /// <summary>
    /// ART-DAQ 封装：设备枚举、AI 电流连续采样 + 最新值 + 环形缓冲。
    /// USB3145A：±20mA，内置分流，差分，默认同步采 ai0:7。
    /// </summary>
    internal sealed class ArtDaqInterop : IDisposable
    {
        public const int ChannelCount = 8;
        public const double MinCurrentAmps = -0.02;
        public const double MaxCurrentAmps = 0.02;
        public const double DefaultSampleRateHz = 10000.0;
        public const double MaxSampleRateHz = 500000.0;
        private const double RingCapacitySeconds = 1.0;

        private readonly object _sync = new();
        private readonly double[] _latestAmps = new double[ChannelCount];
        private readonly bool[] _hasLatest = new bool[ChannelCount];

        private double[] _ring = Array.Empty<double>(); // interleaved: sample * ChannelCount + ch
        private int _ringCapacity;
        private int _ringCount;
        private int _ringWrite;

        private string _deviceName = string.Empty;
        private IntPtr _task = IntPtr.Zero;
        private Thread? _readerThread;
        private volatile bool _runReader;
        private double _sampleRateHz = DefaultSampleRateHz;
        private int _readChunk;
        private bool _disposed;

        public bool Initialized { get; private set; }
        public bool ContinuousRunning { get; private set; }
        public string DeviceName => _deviceName;
        public double SampleRateHz => _sampleRateHz;

        public bool Initialize(string? connectionParameters)
        {
            try
            {
                ArtDaqNative.EnsureLoaded();
                _deviceName = ResolveDeviceName(connectionParameters);
                _sampleRateHz = ParseSampleRateHz(connectionParameters);
                Initialized = !string.IsNullOrWhiteSpace(_deviceName);
                return Initialized;
            }
            catch
            {
                Initialized = false;
                return false;
            }
        }

        public void StartContinuous(double? sampleRateHz = null)
        {
            EnsureInitialized();
            StopContinuous();

            if (sampleRateHz is > 0)
                _sampleRateHz = ClampSampleRate(sampleRateHz.Value);

            _readChunk = Math.Max(100, (int)Math.Round(_sampleRateHz * 0.1));
            AllocateRing(Math.Max(1000, (int)Math.Ceiling(_sampleRateHz * RingCapacitySeconds)));

            var physicalChannel = $"{_deviceName}/ai0:7";

            ArtDaqNative.ThrowIfFailed(
                ArtDaqNative.ArtDAQ_CreateTask(string.Empty, out _task),
                "ArtDAQ_CreateTask");

            try
            {
                ArtDaqNative.ThrowIfFailed(
                    ArtDaqNative.ArtDAQ_CreateAICurrentChan(
                        _task,
                        physicalChannel,
                        string.Empty,
                        ArtDaqNative.ValDiff,
                        MinCurrentAmps,
                        MaxCurrentAmps,
                        ArtDaqNative.ValAmps,
                        ArtDaqNative.ValInternal,
                        0.0,
                        string.Empty),
                    "ArtDAQ_CreateAICurrentChan");

                ArtDaqNative.ThrowIfFailed(
                    ArtDaqNative.ArtDAQ_CfgSampClkTiming(
                        _task,
                        string.Empty,
                        _sampleRateHz,
                        ArtDaqNative.ValRising,
                        ArtDaqNative.ValContSamps,
                        _readChunk),
                    "ArtDAQ_CfgSampClkTiming");

                ArtDaqNative.ThrowIfFailed(
                    ArtDaqNative.ArtDAQ_StartTask(_task),
                    "ArtDAQ_StartTask");
            }
            catch
            {
                ClearTask();
                throw;
            }

            _runReader = true;
            _readerThread = new Thread(ReaderLoop)
            {
                IsBackground = true,
                Name = $"ArtDAQ-{_deviceName}-AI"
            };
            _readerThread.Start();
            ContinuousRunning = true;
        }

        public void StopContinuous()
        {
            _runReader = false;
            var thread = _readerThread;
            _readerThread = null;
            if (thread != null && thread.IsAlive)
            {
                if (!thread.Join(3000))
                {
                    try { if (_task != IntPtr.Zero) ArtDaqNative.ArtDAQ_StopTask(_task); } catch { }
                    thread.Join(2000);
                }
            }

            ClearTask();
            ContinuousRunning = false;
        }

        public bool TryGetLatestAmps(int channelIndex, out double amps)
        {
            amps = 0;
            if (channelIndex < 0 || channelIndex >= ChannelCount)
                return false;

            lock (_sync)
            {
                if (!_hasLatest[channelIndex])
                    return false;
                amps = _latestAmps[channelIndex];
                return true;
            }
        }

        public bool TryGetLatestAmps(int startChannel, int count, out double[] amps)
        {
            amps = Array.Empty<double>();
            if (startChannel < 0 || count <= 0 || startChannel + count > ChannelCount)
                return false;

            lock (_sync)
            {
                var result = new double[count];
                for (var i = 0; i < count; i++)
                {
                    if (!_hasLatest[startChannel + i])
                        return false;
                    result[i] = _latestAmps[startChannel + i];
                }

                amps = result;
                return true;
            }
        }

        /// <summary>
        /// 拷贝环形缓冲最近样本，维度 [sample, channel]，单位 A，时间从旧到新。
        /// </summary>
        public bool TryCopyRecentSamples(out double[,] samplesByIndexThenChannel, out int sampleCount)
        {
            lock (_sync)
            {
                sampleCount = _ringCount;
                if (sampleCount <= 0 || _ringCapacity <= 0)
                {
                    samplesByIndexThenChannel = new double[0, ChannelCount];
                    return false;
                }

                samplesByIndexThenChannel = new double[sampleCount, ChannelCount];
                var start = (_ringWrite - sampleCount + _ringCapacity) % _ringCapacity;
                for (var i = 0; i < sampleCount; i++)
                {
                    var ringIndex = (start + i) % _ringCapacity;
                    var baseOffset = ringIndex * ChannelCount;
                    for (var ch = 0; ch < ChannelCount; ch++)
                        samplesByIndexThenChannel[i, ch] = _ring[baseOffset + ch];
                }

                return true;
            }
        }

        public IReadOnlyList<string> EnumerateDevices()
        {
            ArtDaqNative.EnsureLoaded();

            var buffer = new byte[4096];
            var error = ArtDaqNative.ArtDAQ_GetSystemAttribute(ArtDaqNative.SysDevNames, buffer, buffer.Length);
            if (error < 0)
                return Array.Empty<string>();

            var text = Encoding.Default.GetString(buffer);
            var nullIndex = text.IndexOf('\0');
            if (nullIndex >= 0)
                text = text[..nullIndex];

            return text
                .Split(new[] { ',', ';', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void AllocateRing(int capacitySamples)
        {
            lock (_sync)
            {
                _ringCapacity = Math.Max(1, capacitySamples);
                _ring = new double[_ringCapacity * ChannelCount];
                _ringCount = 0;
                _ringWrite = 0;
                Array.Clear(_latestAmps, 0, _latestAmps.Length);
                Array.Clear(_hasLatest, 0, _hasLatest.Length);
            }
        }

        private void ReaderLoop()
        {
            var buffer = new double[_readChunk * ChannelCount];
            while (_runReader && _task != IntPtr.Zero)
            {
                try
                {
                    var error = ArtDaqNative.ArtDAQ_ReadAnalogF64(
                        _task,
                        _readChunk,
                        1.0,
                        ArtDaqNative.ValGroupByChannel,
                        buffer,
                        (uint)buffer.Length,
                        out var sampsPerChan,
                        IntPtr.Zero);

                    if (!_runReader)
                        break;

                    if (error < 0 || sampsPerChan <= 0)
                        continue;

                    lock (_sync)
                    {
                        for (var s = 0; s < sampsPerChan; s++)
                        {
                            var baseOffset = _ringWrite * ChannelCount;
                            for (var ch = 0; ch < ChannelCount; ch++)
                            {
                                var amps = buffer[ch * sampsPerChan + s];
                                _ring[baseOffset + ch] = amps;
                                _latestAmps[ch] = amps;
                                _hasLatest[ch] = true;
                            }

                            _ringWrite = (_ringWrite + 1) % _ringCapacity;
                            if (_ringCount < _ringCapacity)
                                _ringCount++;
                        }
                    }
                }
                catch
                {
                    if (!_runReader)
                        break;
                    Thread.Sleep(10);
                }
            }
        }

        private void ClearTask()
        {
            if (_task == IntPtr.Zero)
                return;

            try { ArtDaqNative.ArtDAQ_StopTask(_task); } catch { }
            try { ArtDaqNative.ArtDAQ_ClearTask(_task); } catch { }
            _task = IntPtr.Zero;
        }

        private string ResolveDeviceName(string? connectionParameters)
        {
            var preferred = StripConnectionPrefix(connectionParameters);
            var devices = EnumerateDevices();

            if (!string.IsNullOrWhiteSpace(preferred))
            {
                var match = devices.FirstOrDefault(d =>
                    d.Equals(preferred, StringComparison.OrdinalIgnoreCase));
                return match ?? preferred;
            }

            if (devices.Count > 0)
                return devices[0];

            throw new InvalidOperationException(
                "No ART-DAQ device found. Install ART-DAQ, connect USB3145A, and check DMC device name (e.g. Dev1).");
        }

        public static string StripConnectionPrefix(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var text = value.Trim();
            foreach (var prefix in new[] { "ArtDAQ://", "ARTDAQ://", "PCIE://", "USB://" })
            {
                if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    text = text[prefix.Length..].Trim();
                    break;
                }
            }

            var semi = text.IndexOf(';');
            if (semi >= 0)
                text = text[..semi].Trim();

            return text;
        }

        public static double ParseSampleRateHz(string? connectionParameters)
        {
            if (string.IsNullOrWhiteSpace(connectionParameters))
                return DefaultSampleRateHz;

            foreach (var part in connectionParameters.Split(';'))
            {
                var kv = part.Split(new[] { '=' }, 2);
                if (kv.Length != 2)
                    continue;
                if (!kv[0].Trim().Equals("SampleRate", StringComparison.OrdinalIgnoreCase) &&
                    !kv[0].Trim().Equals("Rate", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (double.TryParse(kv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var rate))
                    return ClampSampleRate(rate);
            }

            return DefaultSampleRateHz;
        }

        private static double ClampSampleRate(double rateHz)
        {
            if (rateHz < 1.0) return 1.0;
            if (rateHz > MaxSampleRateHz) return MaxSampleRateHz;
            return rateHz;
        }

        private void EnsureInitialized()
        {
            if (!Initialized || string.IsNullOrWhiteSpace(_deviceName))
                throw new InvalidOperationException("ART-DAQ device is not initialized.");
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            StopContinuous();
            Initialized = false;
            _deviceName = string.Empty;
            _disposed = true;
        }
    }
}
