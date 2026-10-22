using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DeviceCommunication.Protocols.IOCard.ArtTechnology
{
    /// <summary>
    /// ART-DAQ C API entries used by <see cref="ArtDaqInterop"/>. Loaded from Art_DAQ.dll.
    /// </summary>
    internal static class ArtDaqNative
    {
        public const int ValDiff = 10106;
        public const int ValRising = 10280;
        public const int ValContSamps = 10123;
        public const int ValInternal = 10200;
        public const int ValAmps = 10342;
        public const int ValGroupByChannel = 0;
        public const int SysDevNames = 0x193B;

        private const string LibraryName = "Art_DAQ.dll";
        private static readonly object Gate = new();
        private static bool _probed;

        public static void EnsureLoaded()
        {
            if (_probed)
                return;

            lock (Gate)
            {
                if (_probed)
                    return;

                foreach (var directory in CandidateDirectories())
                {
                    var path = Path.Combine(directory, LibraryName);
                    if (!File.Exists(path))
                        continue;

                    SetDllDirectory(directory);
                    if (LoadLibrary(path) != IntPtr.Zero)
                        break;
                }

                _probed = true;
            }
        }

        public static string GetExtendedErrorInfo()
        {
            try
            {
                var buffer = new byte[2048];
                ArtDAQ_GetExtendedErrorInfo(buffer, (uint)buffer.Length);
                var text = Encoding.Default.GetString(buffer);
                var end = text.IndexOf('\0');
                if (end >= 0)
                    text = text[..end];
                return text.Trim();
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

        private static IEnumerable<string> CandidateDirectories()
        {
            var arch = Environment.Is64BitProcess ? "x64" : "win32";
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetEnvironmentVariable("ARTDAQ_ROOT") ?? string.Empty
            };

            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root))
                    continue;

                var install = root.IndexOf("ART-DAQ", StringComparison.OrdinalIgnoreCase) >= 0
                    ? root
                    : Path.Combine(root, "ART Technology", "ART-DAQ");
                yield return Path.Combine(install, "Lib", arch);
                yield return Path.Combine(install, "Bin");
            }
        }

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectory(string path);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string path);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int ArtDAQ_CreateTask(string taskName, out IntPtr taskHandle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int ArtDAQ_StartTask(IntPtr taskHandle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int ArtDAQ_StopTask(IntPtr taskHandle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int ArtDAQ_ClearTask(IntPtr taskHandle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int ArtDAQ_CreateAICurrentChan(
            IntPtr taskHandle,
            string physicalChannel,
            string nameToAssignToChannel,
            int terminalConfig,
            double minVal,
            double maxVal,
            int units,
            int shuntResistorLocation,
            double externalShuntOhms,
            string customScaleName);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int ArtDAQ_CfgSampClkTiming(
            IntPtr taskHandle,
            string source,
            double rate,
            int activeEdge,
            int sampleMode,
            int sampsPerChan);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int ArtDAQ_ReadAnalogF64(
            IntPtr taskHandle,
            int numSampsPerChan,
            double timeout,
            int fillMode,
            double[] readArray,
            uint arraySizeInSamps,
            out int sampsPerChanRead,
            IntPtr reserved);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int ArtDAQ_GetSystemAttribute(int attribute, byte[] value, int bufferSize);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int ArtDAQ_GetExtendedErrorInfo(byte[] errorString, uint bufferSize);
    }
}
