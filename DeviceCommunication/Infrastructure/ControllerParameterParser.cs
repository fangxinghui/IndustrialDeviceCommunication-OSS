using System;
using System.Collections.Generic;
using System.Linq;
using DeviceCommunication.Models;

namespace DeviceCommunication.Infrastructure
{
    /// <summary>
    /// ControllerConfiguration.Parameters 通用解析工具（不含品牌路由逻辑）。
    /// </summary>
    internal static class ControllerParameterParser
    {
        public static string[] Split(ControllerConfiguration configuration)
        {
            return (configuration.Parameters ?? string.Empty)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .ToArray();
        }

        public static Dictionary<string, string> ParseKeyValues(IEnumerable<string> parts)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in parts)
            {
                var kv = part.Split(new[] { '=' }, 2);
                if (kv.Length == 2 && !string.IsNullOrWhiteSpace(kv[0]))
                    values[kv[0].Trim()] = kv[1].Trim();
            }

            return values;
        }

        public static string? GetValue(IReadOnlyDictionary<string, string> values, string key)
            => values.TryGetValue(key, out var value) ? value : null;

        public static int ParseInt(string? value, int defaultValue)
            => int.TryParse(value, out var parsed) ? parsed : defaultValue;

        public static bool TryParseStopBits(string value, out SerialParameters.SerialStopBits stopBits)
        {
            stopBits = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Trim();

            // 必须先匹配数字字面量：Enum.TryParse("1") 会按枚举值 1 解析成 OnePointFive
            switch (value)
            {
                case "1":
                    stopBits = SerialParameters.SerialStopBits.One;
                    return true;
                case "2":
                    stopBits = SerialParameters.SerialStopBits.Two;
                    return true;
                case "1.5":
                    stopBits = SerialParameters.SerialStopBits.OnePointFive;
                    return true;
            }

            return Enum.TryParse(value, ignoreCase: true, out stopBits);
        }

        public static bool TryParseParity(string value, out SerialParameters.SerialParity parity)
        {
            parity = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Trim();

            switch (value.ToUpperInvariant())
            {
                case "N":
                case "0":
                    parity = SerialParameters.SerialParity.None;
                    return true;
                case "O":
                case "1":
                    parity = SerialParameters.SerialParity.Odd;
                    return true;
                case "E":
                case "2":
                    parity = SerialParameters.SerialParity.Even;
                    return true;
            }

            return Enum.TryParse(value, ignoreCase: true, out parity);
        }

        public static string NormalizeSerialPortName(string port)
        {
            if (string.IsNullOrWhiteSpace(port))
                return "COM1";

            port = port.Trim();
            if (port.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                return port.ToUpperInvariant();

            return int.TryParse(port, out _) ? "COM" + port : port;
        }
    }
}
