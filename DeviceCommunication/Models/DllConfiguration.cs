using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using DeviceCommunication.Abstractions;

namespace DeviceCommunication.Models
{
    /// <summary>
    /// DLL 仪表连接参数（软件路径、SDK 目录等），序列化到 instruments.json 的 commoConfigurationJson。
    /// </summary>
    public class DllConfiguration : IConfiguration, INotifyPropertyChanged
    {
        private string? _appPath;
        private string? _dllPath;
        private string? _dllName;
        private int? _instanceIndex;
        private string? _measurementNames;

        /// <summary>厂商软件 exe 完整路径（如 FlexSystem.exe）。</summary>
        public string? AppPath
        {
            get => _appPath;
            set => SetField(ref _appPath, value);
        }

        /// <summary>厂商 API DLL 所在目录（相对程序目录或绝对路径，如 Drivers）。</summary>
        public string? DllPath
        {
            get => _dllPath;
            set => SetField(ref _dllPath, value);
        }

        /// <summary>厂商 API DLL 文件名（如 FlexAPI.dll、FlexAPI2.dll）。</summary>
        public string? DllName
        {
            get => _dllName;
            set => SetField(ref _dllName, value);
        }

        /// <summary>FlexAPI 实例编号（1=FlexAPI.dll，2=FlexAPI2.dll，自动复制）。</summary>
        public int? InstanceIndex
        {
            get => _instanceIndex;
            set => SetField(ref _instanceIndex, value);
        }

        /// <summary>预置测量/曲线名称，逗号分隔。</summary>
        public string? MeasurementNames
        {
            get => _measurementNames;
            set => SetField(ref _measurementNames, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    /// <summary>
    /// 将 <see cref="DllConfiguration"/> 与驱动使用的 key=value 连接字符串互转。
    /// </summary>
    public static class DllConnectionStringBuilder
    {
        public static string Build(DllConfiguration config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(config.AppPath))
                parts.Add($"AppPath={SanitizeAppPath(config.AppPath)}");
            if (!string.IsNullOrWhiteSpace(config.DllPath))
                parts.Add($"DllPath={config.DllPath.Trim()}");
            if (!string.IsNullOrWhiteSpace(config.DllName))
                parts.Add($"DllName={config.DllName.Trim()}");
            if (config.InstanceIndex is > 0)
                parts.Add($"InstanceIndex={config.InstanceIndex.Value.ToString(CultureInfo.InvariantCulture)}");
            if (!string.IsNullOrWhiteSpace(config.MeasurementNames))
                parts.Add($"MeasurementNames={config.MeasurementNames.Trim()}");

            return string.Join(";", parts);
        }

        public static DllConfiguration Parse(string? connectionString)
        {
            var config = new DllConfiguration();
            if (string.IsNullOrWhiteSpace(connectionString))
                return config;

            if (!connectionString.Contains('='))
            {
                config.AppPath = connectionString.Trim();
                return config;
            }

            foreach (var segment in connectionString.Split(new[] { ';', '\n', '\r' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                var index = segment.IndexOf('=');
                if (index <= 0)
                    continue;

                var key = segment[..index].Trim();
                var value = segment[(index + 1)..].Trim().Trim('"');
                switch (key.ToUpperInvariant())
                {
                    case "APPPATH":
                    case "PATH":
                    case "SOFTWAREPATH":
                    case "FLEXSYSTEMPATH":
                    case "FLEXPATH":
                    case "TRUSTSYSTEMPATH":
                        if (string.IsNullOrWhiteSpace(config.AppPath))
                            config.AppPath = SanitizeAppPath(value);
                        break;
                    case "DLLPATH":
                    case "SDKPATH":
                    case "DLLDIRECTORY":
                        config.DllPath = value;
                        break;
                    case "DLLNAME":
                    case "APIDLL":
                    case "FLEXAPIDLL":
                    case "SDKDLL":
                        config.DllName = value;
                        break;
                    case "INSTANCEINDEX":
                    case "INSTANCE":
                    case "DLLINSTANCE":
                    case "APIINSTANCE":
                    case "FLEXINSTANCE":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var instanceIndex))
                            config.InstanceIndex = instanceIndex;
                        break;
                    case "MEASUREMENTNAMES":
                    case "CURVENAMES":
                    case "ITEMS":
                        config.MeasurementNames = value;
                        break;
                }
            }

            return config;
        }

        public static bool LooksLikeDllConnectionString(string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                return false;

            if (!connectionString.Contains('='))
                return connectionString.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

            return connectionString.Contains("AppPath=", StringComparison.OrdinalIgnoreCase)
                   || connectionString.Contains("DllPath=", StringComparison.OrdinalIgnoreCase);
        }

        private static string SanitizeAppPath(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var trimmed = value.Trim().Trim('"');
            if (trimmed.Contains(';') && trimmed.Contains('='))
            {
                foreach (var segment in trimmed.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var index = segment.IndexOf('=');
                    if (index <= 0)
                        continue;

                    var key = segment[..index].Trim();
                    if (key.Equals("AppPath", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("Path", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("SoftwarePath", StringComparison.OrdinalIgnoreCase))
                    {
                        return SanitizeSingleAppPath(segment[(index + 1)..]);
                    }
                }
            }

            return SanitizeSingleAppPath(trimmed);
        }

        private static string SanitizeSingleAppPath(string value)
        {
            var result = value.Trim().Trim('"');
            foreach (var prefix in new[] { "AppPath=", "Path=", "SoftwarePath=" })
            {
                if (result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    result = result[prefix.Length..].Trim().Trim('"');
            }

            var semiIndex = result.IndexOf(';');
            if (semiIndex > 0)
                result = result[..semiIndex].Trim();

            return result;
        }
    }
}
