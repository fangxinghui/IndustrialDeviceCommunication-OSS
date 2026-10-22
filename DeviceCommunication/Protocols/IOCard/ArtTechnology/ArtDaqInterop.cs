using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

namespace DeviceCommunication.Protocols.IOCard.ArtTechnology
{
    /// <summary>
    /// Continuous 8-channel current acquisition on an ART USB3145A through Art_DAQ.dll.
    /// Samples are stored oldest-to-newest for one second, and the latest value of each channel is kept aside.
    /// </summary>
    internal sealed class ArtDaqInterop : IDisposable
    {
        public const int ChannelCount = 8;
        private const double MinCurrentAmps = -0.02;
        private const double MaxCurrentAmps = 0.02;
        private const double DefaultSampleRateHz = 10000.0;
        private const double MaxSampleRateHz = 500000.0;
        private const double RingCapacitySeconds = 1.0;

        private readonly object _gate = new();
        private readonly double[] _latestAmps = new double[ChannelCount];
        private readonly bool[] _hasLatest = new bool[ChannelCount];

        private double[] _ring = Array.Empty<double>();
        private int _ringCapacity;
        private int _ringCount;
        private int _ringWrite;
        private string _deviceName = string.Empty;
        private IntPtr _task = IntPtr.Zero;
        private Thread? _reader;
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
                _deviceName = PickDeviceName(connectionParameters);
                _sampleRateHz = ReadSampleRate(connectionParameters);
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
            RequireReady();
            StopContinuous();
            if (sampleRateHz is > 0)
                _sampleRateHz = ClampRate(sampleRateHz.Value);

            _readChunk = Math.Max(100, (int)Math.Round(_sampleRateHz * 0.1));
            ResetRing(Math.Max(1000, (int)Math.Ceiling(_sampleRateHz * RingCapacitySeconds)));

            ArtDaqNative.ThrowIfFailed(
                ArtDaqNative.ArtDAQ_CreateTask(string.Empty, out _task),
                "ArtDAQ_CreateTask");
            try
            {
                ArtDaqNative.ThrowIfFailed(
                    ArtDaqNative.ArtDAQ_CreateAICurrentChan(
                        _task,
                        $"{_deviceName}/ai0:7",
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
                DropTask();
                throw;
            }

            _runReader = true;
            _reader = new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = $"ArtDAQ-{_deviceName}-AI"
            };
            _reader.Start();
            ContinuousRunning = true;
        }

        public void StopContinuous()
        {
            _runReader = false;
            var thread = _reader;
            _reader = null;
            if (thread != null && thread.IsAlive)
            {
                if (!thread.Join(3000))
                {
                    try
                    {
                        if (_task != IntPtr.Zero)
                            ArtDaqNative.ArtDAQ_StopTask(_task);
                    }
                    catch
                    {
                        // The reader may already have stopped the task.
                    }

                    thread.Join(2000);
                }
            }

            DropTask();
            ContinuousRunning = false;
        }

        public bool TryGetLatestAmps(int channelIndex, out double amps)
        {
            amps = 0;
            if (channelIndex < 0 || channelIndex >= ChannelCount)
                return false;

            lock (_gate)
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

            lock (_gate)
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

        public bool TryCopyRecentSamples(out double[,] samplesByIndexThenChannel, out int sampleCount)
        {
            lock (_gate)
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
                    var origin = ringIndex * ChannelCount;
                    for (var channel = 0; channel < ChannelCount; channel++)
                        samplesByIndexThenChannel[i, channel] = _ring[origin + channel];
                }

                return true;
            }
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

        private void ResetRing(int capacitySamples)
        {
            lock (_gate)
            {
                _ringCapacity = Math.Max(1, capacitySamples);
                _ring = new double[_ringCapacity * ChannelCount];
                _ringCount = 0;
                _ringWrite = 0;
                Array.Clear(_latestAmps, 0, _latestAmps.Length);
                Array.Clear(_hasLatest, 0, _hasLatest.Length);
            }
        }

        private void ReadLoop()
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

                    lock (_gate)
                    {
                        for (var sample = 0; sample < sampsPerChan; sample++)
                        {
                            var origin = _ringWrite * ChannelCount;
                            for (var channel = 0; channel < ChannelCount; channel++)
                            {
                                var amps = buffer[channel * sampsPerChan + sample];
                                _ring[origin + channel] = amps;
                                _latestAmps[channel] = amps;
                                _hasLatest[channel] = true;
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

        private void DropTask()
        {
            if (_task == IntPtr.Zero)
                return;

            try
            {
                ArtDaqNative.ArtDAQ_StopTask(_task);
            }
            catch
            {
                // Stop can fail once the task is already stopped.
            }

            try
            {
                ArtDaqNative.ArtDAQ_ClearTask(_task);
            }
            catch
            {
                // Clear can fail once the driver has released the task.
            }

            _task = IntPtr.Zero;
        }

        private string PickDeviceName(string? connectionParameters)
        {
            var preferred = WithoutPrefix(connectionParameters);
            var devices = ListDevices();
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                var match = devices.FirstOrDefault(d => d.Equals(preferred, StringComparison.OrdinalIgnoreCase));
                return match ?? preferred;
            }

            if (devices.Count > 0)
                return devices[0];

            throw new InvalidOperationException(
                "No ART-DAQ device found. Install ART-DAQ, connect USB3145A, and check DMC device name (e.g. Dev1).");
        }

        private static IReadOnlyList<string> ListDevices()
        {
            ArtDaqNative.EnsureLoaded();
            var buffer = new byte[4096];
            if (ArtDaqNative.ArtDAQ_GetSystemAttribute(ArtDaqNative.SysDevNames, buffer, buffer.Length) < 0)
                return Array.Empty<string>();

            var text = Encoding.Default.GetString(buffer);
            var end = text.IndexOf('\0');
            if (end >= 0)
                text = text[..end];

            return text
                .Split(new[] { ',', ';', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string WithoutPrefix(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var text = value.Trim();
            foreach (var prefix in new[] { "ArtDAQ://", "ARTDAQ://", "PCIE://", "USB://" })
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

        private static double ReadSampleRate(string? connectionParameters)
        {
            if (string.IsNullOrWhiteSpace(connectionParameters))
                return DefaultSampleRateHz;

            foreach (var part in connectionParameters.Split(';'))
            {
                var pair = part.Split(new[] { '=' }, 2);
                if (pair.Length != 2)
                    continue;
                if (!pair[0].Trim().Equals("SampleRate", StringComparison.OrdinalIgnoreCase) &&
                    !pair[0].Trim().Equals("Rate", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (double.TryParse(pair[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var rate))
                    return ClampRate(rate);
            }

            return DefaultSampleRateHz;
        }

        private static double ClampRate(double rateHz)
        {
            if (rateHz < 1.0)
                return 1.0;
            if (rateHz > MaxSampleRateHz)
                return MaxSampleRateHz;
            return rateHz;
        }

        private void RequireReady()
        {
            if (!Initialized || string.IsNullOrWhiteSpace(_deviceName))
                throw new InvalidOperationException("ART-DAQ device is not initialized.");
        }
    }
}
