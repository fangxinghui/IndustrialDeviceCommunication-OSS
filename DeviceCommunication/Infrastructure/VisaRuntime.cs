using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace DeviceCommunication.Infrastructure
{
    /// <summary>
    /// 探测本机是否已安装 VISA 运行时（如 NI-VISA）。
    /// 仅通过原生 DLL 探测，不引用 Ivi.Visa，避免未安装时加载 visaConfMgr.dll 崩溃。
    /// </summary>
    public static class VisaRuntime
    {
        private static readonly object Sync = new();
        private static bool _checked;
        private static bool _isAvailable;
        private static string? _unavailableReason;

        private static readonly string[] RequiredVisaDlls = { "visa64.dll", "visaConfMgr.dll" };

        public const string DefaultUnavailableMessage =
            "未检测到 VISA 运行环境（如 NI-VISA）。请安装 VISA 运行时后再使用 USB 仪器通信。";

        public static bool IsAvailable
        {
            get
            {
                EnsureChecked();
                return _isAvailable;
            }
        }

        public static string UnavailableReason
        {
            get
            {
                EnsureChecked();
                return _unavailableReason ?? DefaultUnavailableMessage;
            }
        }

        public static bool TryEnsureAvailable(out string? errorMessage)
        {
            EnsureChecked();
            if (_isAvailable)
            {
                errorMessage = null;
                return true;
            }

            errorMessage = UnavailableReason;
            return false;
        }

        private static void EnsureChecked()
        {
            if (_checked)
                return;

            lock (Sync)
            {
                if (_checked)
                    return;

                Probe();
                _checked = true;
            }
        }

        private static void Probe()
        {
#if NET9_0_OR_GREATER
            if (!OperatingSystem.IsWindows())
            {
                _isAvailable = false;
                _unavailableReason = "USB VISA 通信当前仅支持 Windows。";
                return;
            }

            foreach (var dll in RequiredVisaDlls)
            {
                if (!TryLocateVisaDll(dll, out var fullPath))
                {
                    _isAvailable = false;
                    _unavailableReason = $"{DefaultUnavailableMessage} (缺少 {dll})";
                    return;
                }

                if (!NativeLibrary.TryLoad(fullPath, out var handle))
                {
                    _isAvailable = false;
                    _unavailableReason = $"{DefaultUnavailableMessage} (无法加载 {dll})";
                    return;
                }

                NativeLibrary.Free(handle);
            }

            _isAvailable = true;
            _unavailableReason = null;
#else
            _isAvailable = false;
            _unavailableReason = "当前目标框架不支持 USB VISA 通信。";
#endif
        }

        private static IEnumerable<string> GetVisaSearchRoots()
        {
            yield return Environment.SystemDirectory;

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                yield return Path.Combine(programFiles, @"IVI Foundation\VISA\Win64\Bin");
                yield return Path.Combine(programFiles, @"National Instruments\Shared\visa64");
            }

            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrWhiteSpace(programFilesX86))
                yield return Path.Combine(programFilesX86, @"IVI Foundation\VISA\WinNT\Bin");
        }

        private static bool TryLocateVisaDll(string fileName, out string fullPath)
        {
            foreach (var root in GetVisaSearchRoots())
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    continue;

                var candidate = Path.Combine(root, fileName);
                if (!File.Exists(candidate))
                    continue;

                fullPath = candidate;
                return true;
            }

            fullPath = fileName;
            return false;
        }
    }
}
