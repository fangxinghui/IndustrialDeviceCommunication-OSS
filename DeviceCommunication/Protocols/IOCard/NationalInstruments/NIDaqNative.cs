using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DeviceCommunication.Protocols.IOCard.NationalInstruments
{
    /// <summary>
    /// NI-DAQmx C API entries used by <see cref="NIDaqInterop"/>. Loaded from nicaiu.dll.
    /// </summary>
    internal static class NIDaqNative
    {
        public const int ValCfgDefault = -1;
        public const int ValRse = 10083;
        public const int ValNrse = 10078;
        public const int ValDiff = 10106;
        public const int ValFiniteSamps = 10178;
        public const int ValRising = 10280;
        public const int ValVolts = 10348;
        public const int ValGroupByChannel = 0;
        public const int ValChanPerLine = 0;
        public const int SysDevNames = 0x193B;

        private const string LibraryName = "nicaiu.dll";

        public static string GetExtendedErrorInfo()
        {
            try
            {
                var buffer = new byte[2048];
                DAQmxGetExtendedErrorInfo(buffer, (uint)buffer.Length);
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
                ? $"{operation} failed with NI-DAQmx error {error}."
                : $"{operation} failed ({error}): {detail}";
            throw new InvalidOperationException(message);
        }

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int DAQmxCreateTask(string taskName, out IntPtr taskHandle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall)]
        public static extern int DAQmxClearTask(IntPtr taskHandle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall)]
        public static extern int DAQmxStartTask(IntPtr taskHandle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int DAQmxResetDevice(string deviceName);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int DAQmxCreateAIVoltageChan(
            IntPtr taskHandle,
            string physicalChannel,
            string nameToAssignToChannel,
            int terminalConfig,
            double minVal,
            double maxVal,
            int units,
            string customScaleName);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int DAQmxCreateAOVoltageChan(
            IntPtr taskHandle,
            string physicalChannel,
            string nameToAssignToChannel,
            double minVal,
            double maxVal,
            int units,
            string customScaleName);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int DAQmxCreateDIChan(
            IntPtr taskHandle,
            string lines,
            string nameToAssignToLines,
            int lineGrouping);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int DAQmxCreateDOChan(
            IntPtr taskHandle,
            string lines,
            string nameToAssignToLines,
            int lineGrouping);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern int DAQmxCfgSampClkTiming(
            IntPtr taskHandle,
            string source,
            double rate,
            int activeEdge,
            int sampleMode,
            ulong sampsPerChan);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall)]
        public static extern int DAQmxReadAnalogScalarF64(
            IntPtr taskHandle,
            double timeout,
            out double value,
            IntPtr reserved);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall)]
        public static extern int DAQmxReadAnalogF64(
            IntPtr taskHandle,
            int numSampsPerChan,
            double timeout,
            int fillMode,
            double[] readArray,
            uint arraySizeInSamps,
            out int sampsPerChanRead,
            IntPtr reserved);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall)]
        public static extern int DAQmxWriteAnalogScalarF64(
            IntPtr taskHandle,
            int autoStart,
            double timeout,
            double value,
            IntPtr reserved);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall)]
        public static extern int DAQmxReadDigitalScalarU32(
            IntPtr taskHandle,
            double timeout,
            out uint value,
            IntPtr reserved);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall)]
        public static extern int DAQmxWriteDigitalScalarU32(
            IntPtr taskHandle,
            int autoStart,
            double timeout,
            uint value,
            IntPtr reserved);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall)]
        public static extern int DAQmxGetSystemInfoAttribute(int attribute, byte[] value, int bufferSize);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall)]
        public static extern int DAQmxGetExtendedErrorInfo(byte[] errorString, uint bufferSize);
    }
}
