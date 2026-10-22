using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CoreShared.Events;
using CoreShared.Interfaces;

namespace DeviceCommunication.Protocols.IOCard.NationalInstruments
{
    /// <summary>
    /// NI-DAQmx 原生 API（nicaiu.dll）封装层。
    ///
    /// 规避 -50405（USB transfer aborted）的关键策略：
    /// 1. 任务常驻：AI/DI/DO/AO 每个通道(线)各建一个 Task 并缓存复用，
    ///    轮询多通道时不再 Clear/Create 循环（Task 频繁重建正是 USB 传输被 abort 的主因）；
    /// 2. 单点读写全部依赖 Read/Write 的 auto-start，不显式 Start/Stop；
    /// 3. 一旦出现 -50405/-50103，设备已进入坏状态，仅重建 Task 无效，
    ///    需 ClearAll + DAQmxResetDevice 后重建（带 5s 节流，避免复位风暴）。
    /// </summary>
    internal sealed class NIDaqInterop : ILogEvent, IDisposable
    {
        private const int ErrorTransferAborted = -50405;
        private const int ErrorResourceReserved = -50103;
        private static readonly TimeSpan ResetThrottle = TimeSpan.FromSeconds(5);

        private readonly object _sync = new();

        // 常驻任务缓存：key → taskHandle
        private readonly Dictionary<string, IntPtr> _aiTasks = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IntPtr> _diTasks = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IntPtr> _doTasks = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IntPtr> _aoTasks = new(StringComparer.OrdinalIgnoreCase);

        private string _deviceName = string.Empty;
        private bool _disposed;
        private DateTime _lastResetUtc = DateTime.MinValue;

        // 当前 AI 选择（ConfigureAiChannel 设定，ReadAiSinglePoint 使用）
        private string? _aiKey;
        private string? _aiChannel;
        private double _aiMin;
        private double _aiMax;
        private int _aiTerm = int.MinValue;

        // 当前 AO 选择
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
            lock (_sync)
            {
                try
                {
                    WriteLog(nameof(Initialize), $"Loading nicaiu.dll, requested device='{deviceName}'.");
                    _deviceName = ResolveDeviceName(deviceName);
                    ClearAllTasks();
                    Initialized = true;
                    WriteLog(nameof(Initialize), $"Initialized successfully. Device='{_deviceName}'.");
                    return true;
                }
                catch (Exception ex)
                {
                    WriteException(nameof(Initialize), ex);
                    Initialized = false;
                    return false;
                }
            }
        }

        public IReadOnlyList<string> EnumerateDevices()
        {
            try
            {
                var buffer = new byte[4096];
                NIDaqNative.ThrowIfFailed(
                    NIDaqNative.DAQmxGetSystemInfoAttribute(NIDaqNative.SysDevNames, buffer, buffer.Length),
                    nameof(NIDaqNative.DAQmxGetSystemInfoAttribute));

                var text = System.Text.Encoding.ASCII.GetString(buffer);
                var nullIndex = text.IndexOf('\0');
                if (nullIndex >= 0)
                    text = text[..nullIndex];

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

        public bool IsDevicePresent(string name)
        {
            return EnumerateDevices().Any(d =>
                d.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        // ═══════════════════ AI ═══════════════════

        public void ConfigureAiChannel(string physicalChannel,
            double minimumValue = -10.0,
            double maximumValue = 10.0,
            int terminalConfig = NIDaqNative.ValCfgDefault)
        {
            lock (_sync)
            {
                EnsureInitialized();

                // 兼容旧托管枚举：2 ≈ Differential
                if (terminalConfig == 2)
                    terminalConfig = NIDaqNative.ValDiff;

                var key = AiKey(physicalChannel, minimumValue, maximumValue, terminalConfig);
                if (_aiTasks.ContainsKey(key))
                {
                    SetCurrentAi(key, physicalChannel, minimumValue, maximumValue, terminalConfig);
                    return;
                }

                CreateAndCacheAiTask(physicalChannel, minimumValue, maximumValue, terminalConfig);
            }
        }

        public double ReadAiSinglePoint(int timeoutMs = 5000)
        {
            lock (_sync)
            {
                EnsureInitialized();
                var task = CurrentAiTask();

                // on-demand 单点：不显式 Start/Stop，由 Read auto-start
                var error = NIDaqNative.DAQmxReadAnalogScalarF64(
                    task, timeoutMs / 1000.0, out var value, IntPtr.Zero);

                if (IsUsbAbortError(error) && TryRecoverDevice("AI read"))
                {
                    // 复位后所有 Task 失效，重建当前 AI 再试一次
                    CreateAndCacheAiTask(_aiChannel!, _aiMin, _aiMax, _aiTerm);
                    task = CurrentAiTask();
                    error = NIDaqNative.DAQmxReadAnalogScalarF64(
                        task, timeoutMs / 1000.0, out value, IntPtr.Zero);
                }

                NIDaqNative.ThrowIfFailed(error, nameof(NIDaqNative.DAQmxReadAnalogScalarF64));
                return value;
            }
        }

        public double[] ReadAiWaveform(int samples, int timeoutMs = 5000)
        {
            lock (_sync)
            {
                EnsureInitialized();
                if (string.IsNullOrWhiteSpace(_aiChannel))
                    throw new InvalidOperationException("AI channel not configured. Call ConfigureAiChannel first.");

                if (samples <= 0)
                    return Array.Empty<double>();

                // 波形读用独立临时 Task（带采样时钟），不污染常驻单点 Task
                var (tempTask, _) = CreateAiTaskCore(_aiChannel, _aiMin, _aiMax, _aiTerm);
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
                        WriteLog(nameof(ReadAiWaveform), "AI waveform read returned empty data.");
                        return Array.Empty<double>();
                    }

                    if (read < data.Length)
                        Array.Resize(ref data, read);

                    return data;
                }
                finally
                {
                    ClearTask(ref tempTask);
                }
            }
        }

        /// <summary>兼容旧调用：常驻缓存模式下波形读已用临时 Task，无需释放。</summary>
        internal void ReleaseTask()
        {
        }

        // ═══════════════════ AO ═══════════════════

        public void ConfigureAoChannel(string physicalChannel,
            double minimumValue = -10.0,
            double maximumValue = 10.0)
        {
            lock (_sync)
            {
                EnsureInitialized();

                var key = $"{physicalChannel}|{minimumValue}|{maximumValue}";
                if (!_aoTasks.ContainsKey(key))
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
                        ClearTask(ref task);
                        NIDaqNative.ThrowIfFailed(error, nameof(NIDaqNative.DAQmxCreateAOVoltageChan));
                    }

                    _aoTasks[key] = task;
                    WriteLog(nameof(ConfigureAoChannel),
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
            lock (_sync)
            {
                try
                {
                    EnsureInitialized();
                    if (_aoKey is null || !_aoTasks.TryGetValue(_aoKey, out var task))
                        throw new InvalidOperationException("NI-DAQmx AO task is not created.");

                    var error = NIDaqNative.DAQmxWriteAnalogScalarF64(task, 1, 10.0, value, IntPtr.Zero);

                    if (IsUsbAbortError(error) && TryRecoverDevice("AO write"))
                    {
                        ConfigureAoChannel(_aoChannel!, _aoMin, _aoMax);
                        _aoTasks.TryGetValue(_aoKey!, out task);
                        error = NIDaqNative.DAQmxWriteAnalogScalarF64(task, 1, 10.0, value, IntPtr.Zero);
                    }

                    NIDaqNative.ThrowIfFailed(error, nameof(NIDaqNative.DAQmxWriteAnalogScalarF64));
                    WriteLog(nameof(WriteAoSinglePoint), $"AO write success: value={value}.");
                    return true;
                }
                catch (Exception ex)
                {
                    WriteException(nameof(WriteAoSinglePoint), ex);
                    return false;
                }
            }
        }

        // ═══════════════════ DI ═══════════════════

        public byte ReadDiPort(string port)
        {
            lock (_sync)
            {
                EnsureInitialized();
                var task = GetOrCreateDiTask(port, NIDaqNative.ValChanForAllLines);

                var buffer = new byte[1];
                var error = NIDaqNative.DAQmxReadDigitalU8(
                    task, 1, 10.0, NIDaqNative.ValGroupByChannel, buffer, 1, out _, IntPtr.Zero);

                if (IsUsbAbortError(error) && TryRecoverDevice("DI port read"))
                {
                    task = GetOrCreateDiTask(port, NIDaqNative.ValChanForAllLines);
                    error = NIDaqNative.DAQmxReadDigitalU8(
                        task, 1, 10.0, NIDaqNative.ValGroupByChannel, buffer, 1, out _, IntPtr.Zero);
                }

                NIDaqNative.ThrowIfFailed(error, nameof(NIDaqNative.DAQmxReadDigitalU8));
                return buffer[0];
            }
        }

        public bool ReadDiLine(string line)
        {
            lock (_sync)
            {
                EnsureInitialized();
                var task = GetOrCreateDiTask(line, NIDaqNative.ValChanPerLine);

                var error = NIDaqNative.DAQmxReadDigitalScalarU32(task, 10.0, out var value, IntPtr.Zero);

                if (IsUsbAbortError(error) && TryRecoverDevice("DI line read"))
                {
                    task = GetOrCreateDiTask(line, NIDaqNative.ValChanPerLine);
                    error = NIDaqNative.DAQmxReadDigitalScalarU32(task, 10.0, out value, IntPtr.Zero);
                }

                NIDaqNative.ThrowIfFailed(error, nameof(NIDaqNative.DAQmxReadDigitalScalarU32));
                return value != 0;
            }
        }

        // ═══════════════════ DO ═══════════════════

        public bool WriteDoPort(string port, byte value)
        {
            lock (_sync)
            {
                try
                {
                    EnsureInitialized();
                    var task = GetOrCreateDoTask(port, NIDaqNative.ValChanForAllLines);

                    var data = new[] { value };
                    var error = NIDaqNative.DAQmxWriteDigitalU8(
                        task, 1, 1, 10.0, NIDaqNative.ValGroupByChannel, data, out _, IntPtr.Zero);

                    if (IsUsbAbortError(error) && TryRecoverDevice("DO port write"))
                    {
                        task = GetOrCreateDoTask(port, NIDaqNative.ValChanForAllLines);
                        error = NIDaqNative.DAQmxWriteDigitalU8(
                            task, 1, 1, 10.0, NIDaqNative.ValGroupByChannel, data, out _, IntPtr.Zero);
                    }

                    NIDaqNative.ThrowIfFailed(error, nameof(NIDaqNative.DAQmxWriteDigitalU8));
                    WriteLog(nameof(WriteDoPort), $"DO port write success: {port} = 0x{value:X2}.");
                    return true;
                }
                catch (Exception ex)
                {
                    WriteException(nameof(WriteDoPort), ex);
                    return false;
                }
            }
        }

        public bool WriteDoLine(string line, bool value)
        {
            lock (_sync)
            {
                try
                {
                    EnsureInitialized();
                    var task = GetOrCreateDoTask(line, NIDaqNative.ValChanPerLine);

                    var error = NIDaqNative.DAQmxWriteDigitalScalarU32(
                        task, 1, 10.0, value ? 1u : 0u, IntPtr.Zero);

                    if (IsUsbAbortError(error) && TryRecoverDevice("DO line write"))
                    {
                        task = GetOrCreateDoTask(line, NIDaqNative.ValChanPerLine);
                        error = NIDaqNative.DAQmxWriteDigitalScalarU32(
                            task, 1, 10.0, value ? 1u : 0u, IntPtr.Zero);
                    }

                    NIDaqNative.ThrowIfFailed(error, nameof(NIDaqNative.DAQmxWriteDigitalScalarU32));
                    WriteLog(nameof(WriteDoLine), $"DO line write success: {line} = {value}.");
                    return true;
                }
                catch (Exception ex)
                {
                    WriteException(nameof(WriteDoLine), ex);
                    return false;
                }
            }
        }

        // ═══════════════════ Task 缓存/恢复 ═══════════════════

        private static string AiKey(string channel, double min, double max, int term)
            => $"{channel}|{min}|{max}|{term}";

        private void SetCurrentAi(string key, string channel, double min, double max, int term)
        {
            _aiKey = key;
            _aiChannel = channel;
            _aiMin = min;
            _aiMax = max;
            _aiTerm = term;
        }

        private IntPtr CurrentAiTask()
        {
            if (_aiKey is null || !_aiTasks.TryGetValue(_aiKey, out var task) || task == IntPtr.Zero)
                throw new InvalidOperationException("NI-DAQmx AI task is not created. Call ConfigureAiChannel first.");
            return task;
        }

        private void CreateAndCacheAiTask(string channel, double min, double max, int term)
        {
            var key = AiKey(channel, min, max, term);
            if (_aiTasks.TryGetValue(key, out var old))
            {
                ClearTask(ref old);
                _aiTasks.Remove(key);
            }

            var (task, _) = CreateAiTaskCore(channel, min, max, term);
            _aiTasks[key] = task;
            SetCurrentAi(key, channel, min, max, term);
        }

        private (IntPtr task, int appliedTerm) CreateAiTaskCore(string channel, double min, double max, int term)
        {
            var candidates = BuildTerminalConfigCandidates(term);
            Exception? lastError = null;

            foreach (var config in candidates)
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

                ClearTask(ref task);
                lastError = new InvalidOperationException(
                    $"{nameof(NIDaqNative.DAQmxCreateAIVoltageChan)} failed ({error}): {NIDaqNative.GetExtendedErrorInfo()}");

                // -200077：该通道不支持请求的接线方式，换下一种再试
                if (error != -200077)
                    break;
            }

            throw lastError ?? new InvalidOperationException($"Failed to configure AI channel '{channel}'.");
        }

        private static int[] BuildTerminalConfigCandidates(int preferred)
        {
            var ordered = preferred == NIDaqNative.ValCfgDefault
                ? new[]
                {
                    NIDaqNative.ValCfgDefault,
                    NIDaqNative.ValRse,
                    NIDaqNative.ValNrse,
                    NIDaqNative.ValDiff
                }
                : new[]
                {
                    preferred,
                    NIDaqNative.ValCfgDefault,
                    NIDaqNative.ValRse,
                    NIDaqNative.ValNrse,
                    NIDaqNative.ValDiff
                };

            return ordered.Distinct().ToArray();
        }

        private IntPtr GetOrCreateDiTask(string lines, int grouping)
        {
            if (_diTasks.TryGetValue(lines, out var existing) && existing != IntPtr.Zero)
                return existing;

            NIDaqNative.ThrowIfFailed(
                NIDaqNative.DAQmxCreateTask(string.Empty, out var task),
                nameof(NIDaqNative.DAQmxCreateTask));

            var error = NIDaqNative.DAQmxCreateDIChan(task, lines, string.Empty, grouping);
            if (error < 0)
            {
                ClearTask(ref task);
                NIDaqNative.ThrowIfFailed(error, nameof(NIDaqNative.DAQmxCreateDIChan));
            }

            _diTasks[lines] = task;
            return task;
        }

        private IntPtr GetOrCreateDoTask(string lines, int grouping)
        {
            if (_doTasks.TryGetValue(lines, out var existing) && existing != IntPtr.Zero)
                return existing;

            NIDaqNative.ThrowIfFailed(
                NIDaqNative.DAQmxCreateTask(string.Empty, out var task),
                nameof(NIDaqNative.DAQmxCreateTask));

            var error = NIDaqNative.DAQmxCreateDOChan(task, lines, string.Empty, grouping);
            if (error < 0)
            {
                ClearTask(ref task);
                NIDaqNative.ThrowIfFailed(error, nameof(NIDaqNative.DAQmxCreateDOChan));
            }

            _doTasks[lines] = task;
            return task;
        }

        private static bool IsUsbAbortError(int error) =>
            error == ErrorTransferAborted || error == ErrorResourceReserved;

        /// <summary>
        /// -50405 后设备处于坏状态：清掉全部 Task 并 ResetDevice。
        /// 带节流：距上次复位不足 5s 时只清 Task 不复位（避免复位风暴反而加剧 abort）。
        /// </summary>
        private bool TryRecoverDevice(string reason)
        {
            ClearAllTasks();

            var now = DateTime.UtcNow;
            if (now - _lastResetUtc < ResetThrottle)
            {
                WriteLog(nameof(TryRecoverDevice),
                    $"USB abort during {reason}; tasks cleared (device reset throttled).");
                return true;
            }

            _lastResetUtc = now;
            try
            {
                var error = NIDaqNative.DAQmxResetDevice(_deviceName);
                if (error < 0)
                {
                    WriteLog(nameof(TryRecoverDevice),
                        $"DAQmxResetDevice('{_deviceName}') after {reason} returned {error}: {NIDaqNative.GetExtendedErrorInfo()}");
                    return false;
                }

                // 给设备一点时间恢复枚举（仅在罕见的恢复路径上）
                Thread.Sleep(200);
                WriteLog(nameof(TryRecoverDevice),
                    $"Device '{_deviceName}' reset after USB abort during {reason}.");
                return true;
            }
            catch (Exception ex)
            {
                WriteLog(nameof(TryRecoverDevice), $"DAQmxResetDevice threw: {ex.Message}");
                return false;
            }
        }

        private void ClearAllTasks()
        {
            foreach (var dict in new[] { _aiTasks, _diTasks, _doTasks, _aoTasks })
            {
                foreach (var handle in dict.Values)
                {
                    var task = handle;
                    ClearTask(ref task);
                }
                dict.Clear();
            }
        }

        /// <summary>只 Clear（内部含停止），不先 Stop，减少多余的 USB abort。</summary>
        private static void ClearTask(ref IntPtr task)
        {
            if (task == IntPtr.Zero)
                return;

            try { NIDaqNative.DAQmxClearTask(task); } catch { }
            task = IntPtr.Zero;
        }

        // ═══════════════════ 设备名解析/工具 ═══════════════════

        private string ResolveDeviceName(string? deviceName)
        {
            var preferred = StripConnectionPrefix(deviceName);
            var devices = EnumerateDevices();

            if (!string.IsNullOrWhiteSpace(preferred))
            {
                var match = devices.FirstOrDefault(d =>
                    d.Equals(preferred, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match;

                if (devices.Count == 0)
                {
                    WriteLog(nameof(ResolveDeviceName),
                        $"Device list empty; using requested name '{preferred}'.");
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

        private static string StripConnectionPrefix(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var text = value.Trim();
            foreach (var prefix in new[] { "NIDAQ://", "NI-DAQ://", "NI://", "DAQ://" })
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

        private void EnsureInitialized()
        {
            if (!Initialized)
                throw new InvalidOperationException("NIDaqInterop not initialized. Call Initialize() first.");
        }

        private void WriteLog(string method, string message)
        {
            OnExecuteLog?.Invoke(this, new ExecuteLogEvent
            {
                ClassName = nameof(NIDaqInterop),
                MethodName = method,
                LogMessage = message
            });
        }

        private void WriteException(string method, Exception ex)
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

        public void Dispose()
        {
            if (_disposed)
                return;

            lock (_sync)
            {
                if (_disposed)
                    return;

                var deviceName = _deviceName;
                ClearAllTasks();

                if (Initialized)
                    WriteLog(nameof(Dispose), $"Disposed. Device='{deviceName}'.");

                Initialized = false;
                _deviceName = string.Empty;
                _disposed = true;
            }
        }
    }
}
