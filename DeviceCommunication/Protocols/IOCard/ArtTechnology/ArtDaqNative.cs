using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DeviceCommunication.Protocols.IOCard.ArtTechnology
{
    /// <summary>
    /// ART-DAQ 原生 API 最小 P/Invoke（AI 电流：按需单点 + 连续采样）。
    /// 运行时从 ART-DAQ 安装目录加载 Art_DAQ.dll。
    /// </summary>
    internal static class ArtDaqNative
    {
        private static readonly object LoadLock = new();
        private static bool _dllReady;

        public const int ValCfgDefault = -1;
        public const int ValDiff = 10106;
        public const int ValInternal = 10200;
        public const int ValAmps = 10342;
        public const int ValGroupByChannel = 0;
        public const int ValRising = 10280;
        public const int ValContSamps = 10123;
        public const int ValAuto = -1;
        public const int SysDevNames = 0x193B;

        public static void EnsureLoaded()
        {
            if (_dllReady)
                return;

            lock (LoadLock)
            {
                if (_dllReady)
                    return;

                TryPreloadArtDaqDll();
                _dllReady = true;
            }
        }

        private static void TryPreloadArtDaqDll()
        {
            foreach (var dir in GetCandidateLibDirectories())
            {
                var dllPath = Path.Combine(dir, "Art_DAQ.dll");
                if (!File.Exists(dllPath))
                    continue;

                SetDllDirectory(dir);
                var handle = LoadLibrary(dllPath);
                if (handle != IntPtr.Zero)
                    return;
            }
        }

        private static string[] GetCandidateLibDirectories()
        {
            var arch = Environment.Is64BitProcess ? "x64" : "win32";
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetEnvironmentVariable("ARTDAQ_ROOT") ?? string.Empty
            };

            var list = new System.Collections.Generic.List<string>();
            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root))
                    continue;

                var baseDir = root.IndexOf("ART-DAQ", StringComparison.OrdinalIgnoreCase) >= 0
                    ? root
                    : Path.Combine(root, "ART Technology", "ART-DAQ");

                list.Add(Path.Combine(baseDir, "Lib", arch));
                list.Add(Path.Combine(baseDir, "Bin"));
            }

            return list.ToArray();
        }

        public static string GetExtendedErrorInfo()
        {
            try
            {
                var buffer = new byte[2048];
                ArtDAQ_GetExtendedErrorInfo(buffer, (uint)buffer.Length);
                var text = Encoding.Default.GetString(buffer);
                var nullIndex = text.IndexOf('\0');
                return nullIndex >= 0 ? text[..nullIndex].Trim() : text.Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        public static void ThrowIfFailed(int error, string operation)
        {
            if (error >= 0)
                return;

            var detail = GetExtendedErrorInfo();
            var message = string.IsNullOrWhiteSpace(detail)
                ? $"{operation} failed with ART-DAQ error {error}."
                : $"{operation} failed ({error}): {detail}";
            throw new InvalidOperationException(message);
        }

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("Art_DAQ.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int ArtDAQ_CreateTask(string taskName, out IntPtr taskHandle);

        [DllImport("Art_DAQ.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int ArtDAQ_StartTask(IntPtr taskHandle);

        [DllImport("Art_DAQ.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int ArtDAQ_StopTask(IntPtr taskHandle);

        [DllImport("Art_DAQ.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int ArtDAQ_ClearTask(IntPtr taskHandle);

        [DllImport("Art_DAQ.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int ArtDAQ_CreateAICurrentChan(
            IntPtr taskHandle,
            string physicalChannel,
            string nameToAssignToChannel,
            int terminalConfig,
            double minVal,
            double maxVal,
            int units,
            int shuntResistorLoc,
            double extShuntResistorVal,
            string customScaleName);

        [DllImport("Art_DAQ.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int ArtDAQ_CfgSampClkTiming(
            IntPtr taskHandle,
            string source,
            double rate,
            int activeEdge,
            int sampleMode,
            int sampsPerChan);

        [DllImport("Art_DAQ.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int ArtDAQ_ReadAnalogScalarF64(
            IntPtr taskHandle,
            double timeout,
            out double value,
            IntPtr reserved);

        [DllImport("Art_DAQ.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int ArtDAQ_ReadAnalogF64(
            IntPtr taskHandle,
            int numSampsPerChan,
            double timeout,
            int fillMode,
            double[] readArray,
            uint arraySizeInSamps,
            out int sampsPerChanRead,
            IntPtr reserved);

        [DllImport("Art_DAQ.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int ArtDAQ_GetSystemAttribute(int attributeType, byte[] attribute, int bufferSize);

        [DllImport("Art_DAQ.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int ArtDAQ_GetExtendedErrorInfo(byte[] errorString, uint bufferSize);
    }
}
