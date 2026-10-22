using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using CoreShared.Events;
using CoreShared.Interfaces;

namespace DeviceCommunication.Protocols.IOCard.NationalInstruments
{
    /// <summary>
    /// Keeps one NI-DAQmx task per analog channel or digital line and reads or writes through nicaiu.dll.
    /// USB transfer abort (-50405) and resource reserved (-50103) clear those tasks and, at most once
    /// every five seconds, reset the device before the call is retried.
    /// </summary>
    internal sealed class NIDaqInterop : ILogEvent, IDisposable
    {
        private const int UsbTransferAborted = -50405;
        private const int ResourceReserved = -50103;
        private const int UnsupportedTerminal = -200077;
        private static readonly TimeSpan ResetInterval = TimeSpan.FromSeconds(5);

        private readonly object _gate = new();
        private readonly Dictionary<string, IntPtr> _analogInputs = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IntPtr> _analogOutputs = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IntPtr> _digitalInputs = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IntPtr> _digitalOutputs = new(StringComparer.OrdinalIgnoreCase);

        private string _deviceName = string.Empty;
        private bool _disposed;
        private DateTime _lastResetUtc = DateTime.MinValue;
        private string? _aiKey;
        private string? _aiChannel;
        private double _aiMin;
        private double _aiMax;
        private int _aiTerminal = int.MinValue;
        private string? _aoKey;
        private string? _aoChannel;
        private double _aoMin;
        private double _aoMax;

        public event EventHandler<ExceptionEvent>? OnException;
        public event EventHandler<ExecuteLogEvent>? OnExecuteLog;

        public bool Initialized { get; private set; }
        public string DeviceName => _deviceName;

        public bool Initialize(string deviceName)
        {
            lock (_gate)
            {
                try
                {
                    PublishLog(nameof(Initialize), $"Loading nicaiu.dll, requested device='{deviceName}'.");
                    _deviceName = PickDeviceName(deviceName);
                    ReleaseEveryTask();
                    Initialized = true;
                    PublishLog(nameof(Initialize), $"Initialized successfully. Device='{_deviceName}'.");
                    return true;
                }
                catch (Exception ex)
                {
                    PublishException(nameof(Initialize), ex);
                    Initialized = false;
                    return false;
                }
            }
        }

        public void ConfigureAiChannel(
            string physicalChannel,
            double minimumValue = -10.0,
            double maximumValue = 10.0,
            int terminalConfig = NIDaqNative.ValCfgDefault)
        {
            lock (_gate)
            {
                RequireReady();
                if (terminalConfig == 2)
                    terminalConfig = NIDaqNative.ValDiff;

                var key = AiKey(physicalChannel, minimumValue, maximumValue, terminalConfig);
                if (_analogInputs.ContainsKey(key))
                {
                    RememberAi(key, physicalChannel, minimumValue, maximumValue, terminalConfig);
                    return;
                }

                OpenAndStoreAi(physicalChannel, minimumValue, maximumValue, terminalConfig);
            }
        }

        public double ReadAiSinglePoint(int timeoutMs = 5000)
        {
            lock (_gate)
            {
                RequireReady();
                var task = AiTaskOrThrow();
                var error = NIDaqNative.DAQmxReadAnalogScalarF64(
                    task, timeoutMs / 1000.0, out var value, IntPtr.Zero);

                if (IsAbort(error) && ResetAfterAbort("AI read"))
                {
                    OpenAndStoreAi(_aiChannel!, _aiMin, _aiMax, _aiTerminal);
                    task = AiTaskOrThrow();
                    error = NIDaqNative.DAQmxReadAnalogScalarF64(
                        task, timeoutMs / 1000.0, out value, IntPtr.Zero);
                }

                NIDaqNative.ThrowIfFailed(error, nameof(NIDaqNative.DAQmxReadAnalogScalarF64));
                return value;
            }
        }

        public double[] ReadAiWaveform(int samples, int timeoutMs = 5000)
        {
            lock (_gate)
            {
                RequireReady();
                if (string.IsNullOrWhiteSpace(_aiChannel))
                    throw new InvalidOperationException("AI channel not configured. Call ConfigureAiChannel first.");

                if (samples <= 0)
                    return Array.Empty<double>();

                var (tempTask, _) = OpenAiTask(_aiChannel, _aiMin, _aiMax, _aiTerminal);
                try
                {
                    NIDaqNative.ThrowIfFailed(
                        NIDaqNative.DAQmxCfgSampClkTiming(
                            tempTask,
                            string.Empty,
                            10000.0,
                            NIDaqNative.ValRising,
                            NIDaqNative.ValFiniteSamps,
                            (ulong)samples),
                        nameof(NIDaqNative.DAQmxCfgSampClkTiming));
                    NIDaqNative.ThrowIfFailed(
                        NIDaqNative.DAQmxStartTask(tempTask),
                        nameof(NIDaqNative.DAQmxStartTask));

                    var data = new double[samples];
                    NIDaqNative.ThrowIfFailed(
                        NIDaqNative.DAQmxReadAnalogF64(
                            tempTask,
                            samples,
                            timeoutMs / 1000.0,
                            NIDaqNative.ValGroupByChannel,
                            data,
                            (uint)data.Length,
                            out var read,
                            IntPtr.Zero),
                        nameof(NIDaqNative.DAQmxReadAnalogF64));

                    if (read <= 0)
                    {
                        PublishLog(nameof(ReadAiWaveform), "AI waveform read returned empty data.");
                        return Array.Empty<double>();
                    }

                    if (read < data.Length)
                        Array.Resize(ref data, read);
                    return data;
                }
                finally
                {
                    ReleaseOne(ref tempTask);
                }
            }
        }

        internal void ReleaseTask()
        {
        }

        public void ConfigureAoChannel(
            string physicalChannel,
            double minimumValue = -10.0,
            double maximumValue = 10.0)
        {
            lock (_gate)
            {
                RequireReady();
                var key = $"{physicalChannel}|{minimumValue}|{maximumValue}";
                if (!_analogOutputs.ContainsKey(key))
                {
                    NIDaqNative.ThrowIfFailed(
                        NIDaqNative.DAQmxCreateTask(string.Empty, out var task),
                        nameof(NIDaqNative.DAQmxCreateTask));
                    var error = NIDaqNative.DAQmxCreateAOVoltageChan(
                        task,
                        physicalChannel,
                        string.Empty,
                        minimumValue,
                        maximumValue,
                        NIDaqNative.ValVolts,
                        string.Empty);
                    if (error < 0)
                    {
                        ReleaseOne(ref task);
                        NIDaqNative.ThrowIfFailed(error, nameof(NIDaqNative.DAQmxCreateAOVoltageChan));
                    }

                    _analogOutputs[key] = task;
                    PublishLog(
                        nameof(ConfigureAoChannel),
                        $"AO channel configured: {physicalChannel}, range=[{minimumValue}, {maximumValue}].");
                }

                _aoKey = key;
                _aoChannel = physicalChannel;
                _aoMin = minimumValue;
                _aoMax = maximumValue;
            }
        }

        public bool WriteAoSinglePoint(double value)
        {
            lock (_gate)
            {
                try
                {
                    RequireReady();
                    if (_aoKey is null || !_analogOutputs.TryGetValue(_aoKey, out var task))
                        throw new InvalidOperationException("NI-DAQmx AO task is not created.");

                    var error = NIDaqNative.DAQmxWriteAnalogScalarF64(task, 1, 10.0, value, IntPtr.Zero);
                    if (IsAbort(error) && ResetAfterAbort("AO write"))
                    {
                        ConfigureAoChannel(_aoChannel!, _aoMin, _aoMax);
                        _analogOutputs.TryGetValue(_aoKey!, out task);
                        error = NIDaqNative.DAQmxWriteAnalogScalarF64(task, 1, 10.0, value, IntPtr.Zero);
                    }

                    NIDaqNative.ThrowIfFailed(error, nameof(NIDaqNative.DAQmxWriteAnalogScalarF64));
                    PublishLog(nameof(WriteAoSinglePoint), $"AO write success: value={value}.");
                    return true;
                }
                catch (Exception ex)
                {
                    PublishException(nameof(WriteAoSinglePoint), ex);
                    return false;
                }
            }
        }

        public bool ReadDiLine(string line)
        {
            lock (_gate)
            {
                RequireReady();
                var task = DigitalInputTask(line);
                var error = NIDaqNative.DAQmxReadDigitalScalarU32(task, 10.0, out var value, IntPtr.Zero);
                if (IsAbort(error) && ResetAfterAbort("DI line read"))
                {
                    task = DigitalInputTask(line);
                    error = NIDaqNative.DAQmxReadDigitalScalarU32(task, 10.0, out value, IntPtr.Zero);
                }

                NIDaqNative.ThrowIfFailed(error, nameof(NIDaqNative.DAQmxReadDigitalScalarU32));
                return value != 0;
            }
        }

        public bool WriteDoLine(string line, bool value)
        {
            lock (_gate)
            {
                try
                {
                    RequireReady();
                    var task = DigitalOutputTask(line);
                    var error = NIDaqNative.DAQmxWriteDigitalScalarU32(
                        task, 1, 10.0, value ? 1u : 0u, IntPtr.Zero);
                    if (IsAbort(error) && ResetAfterAbort("DO line write"))
                    {
                        task = DigitalOutputTask(line);
                        error = NIDaqNative.DAQmxWriteDigitalScalarU32(
                            task, 1, 10.0, value ? 1u : 0u, IntPtr.Zero);
                    }

                    NIDaqNative.ThrowIfFailed(error, nameof(NIDaqNative.DAQmxWriteDigitalScalarU32));
                    PublishLog(nameof(WriteDoLine), $"DO line write success: {line} = {value}.");
                    return true;
                }
                catch (Exception ex)
                {
                    PublishException(nameof(WriteDoLine), ex);
                    return false;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            lock (_gate)
            {
                if (_disposed)
                    return;

                var deviceName = _deviceName;
                ReleaseEveryTask();
                if (Initialized)
                    PublishLog(nameof(Dispose), $"Disposed. Device='{deviceName}'.");

                Initialized = false;
                _deviceName = string.Empty;
                _disposed = true;
            }
        }

        private static string AiKey(string channel, double min, double max, int terminal)
            => $"{channel}|{min}|{max}|{terminal}";

        private void RememberAi(string key, string channel, double min, double max, int terminal)
        {
            _aiKey = key;
            _aiChannel = channel;
            _aiMin = min;
            _aiMax = max;
            _aiTerminal = terminal;
        }

        private IntPtr AiTaskOrThrow()
        {
            if (_aiKey is null || !_analogInputs.TryGetValue(_aiKey, out var task) || task == IntPtr.Zero)
                throw new InvalidOperationException("NI-DAQmx AI task is not created. Call ConfigureAiChannel first.");
            return task;
        }

        private void OpenAndStoreAi(string channel, double min, double max, int terminal)
        {
            var key = AiKey(channel, min, max, terminal);
            if (_analogInputs.TryGetValue(key, out var previous))
            {
                ReleaseOne(ref previous);
                _analogInputs.Remove(key);
            }

            var (task, _) = OpenAiTask(channel, min, max, terminal);
            _analogInputs[key] = task;
            RememberAi(key, channel, min, max, terminal);
        }

        private (IntPtr task, int appliedTerminal) OpenAiTask(string channel, double min, double max, int terminal)
        {
            Exception? lastError = null;
            foreach (var config in TerminalCandidates(terminal))
            {
                NIDaqNative.ThrowIfFailed(
                    NIDaqNative.DAQmxCreateTask(string.Empty, out var task),
                    nameof(NIDaqNative.DAQmxCreateTask));
                var error = NIDaqNative.DAQmxCreateAIVoltageChan(
                    task,
                    channel,
                    string.Empty,
                    config,
                    min,
                    max,
                    NIDaqNative.ValVolts,
                    string.Empty);
                if (error >= 0)
                    return (task, config);

                ReleaseOne(ref task);
                lastError = new InvalidOperationException(
                    $"{nameof(NIDaqNative.DAQmxCreateAIVoltageChan)} failed ({error}): {NIDaqNative.GetExtendedErrorInfo()}");
                if (error != UnsupportedTerminal)
                    break;
            }

            throw lastError ?? new InvalidOperationException($"Failed to configure AI channel '{channel}'.");
        }

        private static int[] TerminalCandidates(int preferred)
        {
            var fallback = new[]
            {
                NIDaqNative.ValCfgDefault,
                NIDaqNative.ValRse,
                NIDaqNative.ValNrse,
                NIDaqNative.ValDiff
            };
            if (preferred == NIDaqNative.ValCfgDefault)
                return fallback;

            return new[] { preferred }.Concat(fallback).Distinct().ToArray();
        }

        private IntPtr DigitalInputTask(string lines)
        {
            if (_digitalInputs.TryGetValue(lines, out var existing) && existing != IntPtr.Zero)
                return existing;

            NIDaqNative.ThrowIfFailed(
                NIDaqNative.DAQmxCreateTask(string.Empty, out var task),
                nameof(NIDaqNative.DAQmxCreateTask));
            var error = NIDaqNative.DAQmxCreateDIChan(task, lines, string.Empty, NIDaqNative.ValChanPerLine);
            if (error < 0)
            {
                ReleaseOne(ref task);
                NIDaqNative.ThrowIfFailed(error, nameof(NIDaqNative.DAQmxCreateDIChan));
            }

            _digitalInputs[lines] = task;
            return task;
        }

        private IntPtr DigitalOutputTask(string lines)
        {
            if (_digitalOutputs.TryGetValue(lines, out var existing) && existing != IntPtr.Zero)
                return existing;

            NIDaqNative.ThrowIfFailed(
                NIDaqNative.DAQmxCreateTask(string.Empty, out var task),
                nameof(NIDaqNative.DAQmxCreateTask));
            var error = NIDaqNative.DAQmxCreateDOChan(task, lines, string.Empty, NIDaqNative.ValChanPerLine);
            if (error < 0)
            {
                ReleaseOne(ref task);
                NIDaqNative.ThrowIfFailed(error, nameof(NIDaqNative.DAQmxCreateDOChan));
            }

            _digitalOutputs[lines] = task;
            return task;
        }

        private static bool IsAbort(int error) => error == UsbTransferAborted || error == ResourceReserved;

        private bool ResetAfterAbort(string reason)
        {
            ReleaseEveryTask();
            var now = DateTime.UtcNow;
            if (now - _lastResetUtc < ResetInterval)
            {
                PublishLog("TryRecoverDevice", $"USB abort during {reason}; tasks cleared (device reset throttled).");
                return true;
            }

            _lastResetUtc = now;
            try
            {
                var error = NIDaqNative.DAQmxResetDevice(_deviceName);
                if (error < 0)
                {
                    PublishLog(
                        "TryRecoverDevice",
                        $"DAQmxResetDevice('{_deviceName}') after {reason} returned {error}: {NIDaqNative.GetExtendedErrorInfo()}");
                    return false;
                }

                Thread.Sleep(200);
                PublishLog("TryRecoverDevice", $"Device '{_deviceName}' reset after USB abort during {reason}.");
                return true;
            }
            catch (Exception ex)
            {
                PublishLog("TryRecoverDevice", $"DAQmxResetDevice threw: {ex.Message}");
                return false;
            }
        }

        private void ReleaseEveryTask()
        {
            foreach (var tasks in new[] { _analogInputs, _digitalInputs, _digitalOutputs, _analogOutputs })
            {
                foreach (var handle in tasks.Values)
                {
                    var task = handle;
                    ReleaseOne(ref task);
                }

                tasks.Clear();
            }
        }

        private static void ReleaseOne(ref IntPtr task)
        {
            if (task == IntPtr.Zero)
                return;

            try
            {
                NIDaqNative.DAQmxClearTask(task);
            }
            catch
            {
                // Clearing a task that the driver already dropped is not fatal.
            }

            task = IntPtr.Zero;
        }

        private string PickDeviceName(string? deviceName)
        {
            var preferred = WithoutPrefix(deviceName);
            var devices = ListDevices();
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                var match = devices.FirstOrDefault(d => d.Equals(preferred, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match;

                if (devices.Count == 0)
                {
                    PublishLog("ResolveDeviceName", $"Device list empty; using requested name '{preferred}'.");
                    return preferred;
                }

                throw new InvalidOperationException(
                    $"NI DAQ device '{preferred}' not found. Available: {string.Join(", ", devices)}");
            }

            if (devices.Count == 0)
                throw new InvalidOperationException(
                    "No NI DAQ devices available. Check NI-DAQmx driver and MAX device name (e.g. Dev1).");

            return devices[0];
        }

        private static IReadOnlyList<string> ListDevices()
        {
            try
            {
                var buffer = new byte[4096];
                NIDaqNative.ThrowIfFailed(
                    NIDaqNative.DAQmxGetSystemInfoAttribute(NIDaqNative.SysDevNames, buffer, buffer.Length),
                    nameof(NIDaqNative.DAQmxGetSystemInfoAttribute));
                var text = Encoding.ASCII.GetString(buffer);
                var end = text.IndexOf('\0');
                if (end >= 0)
                    text = text[..end];

                return text
                    .Split(new[] { ',', '\0' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .OrderBy(s => s)
                    .ToList();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static string WithoutPrefix(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var text = value.Trim();
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

        private void RequireReady()
        {
            if (!Initialized)
                throw new InvalidOperationException("NIDaqInterop not initialized. Call Initialize() first.");
        }

        private void PublishLog(string method, string message)
        {
            OnExecuteLog?.Invoke(this, new ExecuteLogEvent
            {
                ClassName = nameof(NIDaqInterop),
                MethodName = method,
                LogMessage = message
            });
        }

        private void PublishException(string method, Exception ex)
        {
            var message = ex.InnerException == null
                ? $"{ex.Message} (0x{ex.HResult:X8})"
                : $"{ex.Message} (0x{ex.HResult:X8}) -> {ex.InnerException.Message}";
            OnException?.Invoke(this, new ExceptionEvent
            {
                ClassName = nameof(NIDaqInterop),
                MethodName = method,
                ErrorMessage = message
            });
        }
    }
}
