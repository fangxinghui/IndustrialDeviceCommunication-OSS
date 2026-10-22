using System;
using System.Linq;
using DeviceCommunication.Models;

namespace DeviceCommunication.Infrastructure
{
    /// <summary>
    /// 将通用控制器元数据与连接字符串构建为驱动层配置。
    /// </summary>
    public static class ControllerConfigurationBuilder
    {
        public static ControllerConfiguration Build(
            string? brandName,
            string? model,
            string? description,
            ControllerType controllerType,
            string? connection,
            int updateInterval = 100)
        {
            var (connectionType, parameters) = ParseConnection(connection, brandName);

            return new ControllerConfiguration
            {
                BrandName = brandName,
                Model = model,
                Description = description,
                ControllerType = controllerType,
                ConnectionType = connectionType,
                Parameters = parameters,
                UpdateInterval = updateInterval
            };
        }

        private static (ConnectionType connType, string parameters) ParseConnection(
            string? connection,
            string? brandName)
        {
            if (string.IsNullOrWhiteSpace(connection))
                return (ConnectionType.Network, string.Empty);

            if (connection.StartsWith("ModbusTcp://", StringComparison.OrdinalIgnoreCase))
            {
                var body = connection["ModbusTcp://".Length..];
                var parts = body.Split(';');
                var ipPort = parts[0].Split(':');
                var ip = ipPort.Length > 0 ? ipPort[0] : "127.0.0.1";
                var port = ipPort.Length > 1 ? ipPort[1] : "502";
                var slave = ExtractValue(parts, "SlaveId", "1");
                return (ConnectionType.Network, $"{ip};{port};{slave};0");
            }

            if (connection.StartsWith("ModbusRtu://", StringComparison.OrdinalIgnoreCase))
            {
                var body = connection["ModbusRtu://".Length..];
                var parts = body.Split(';');
                var portName = parts.Length > 0 ? parts[0].Replace("COM", "") : "1";
                var baud = ExtractValue(parts, "Baud", "9600");
                var data = ExtractValue(parts, "Data", "8");
                var parity = ExtractValue(parts, "Parity", "None");
                var stop = ExtractValue(parts, "Stop", "1");
                var slave = ExtractValue(parts, "SlaveId", "1");
                return (ConnectionType.SerialPort, $"{portName};{baud};{data};{stop};{parity};{slave};0");
            }

            if (connection.StartsWith("Tcp://", StringComparison.OrdinalIgnoreCase))
            {
                var body = connection["Tcp://".Length..];
                var parts = body.Split(';');
                var ipPort = parts[0].Split(':');
                var ip = ipPort.Length > 0 ? ipPort[0] : "127.0.0.1";
                var port = ipPort.Length > 1 ? ipPort[1] : "102";

                if (string.Equals(brandName, "Siemens", StringComparison.OrdinalIgnoreCase))
                {
                    var rack = ExtractValue(parts, "Rack", "0");
                    var slot = ExtractValue(parts, "Slot", "2");
                    return (ConnectionType.Network, $"{ip};{port};{rack};{slot}");
                }

                var extra = parts.Length > 1 ? ";" + string.Join(";", parts.Skip(1)) : string.Empty;
                return (ConnectionType.Network, $"{ip};{port}{extra}");
            }

            // 研华 BDaq：UI 存 PCIE://PCIE-1756,BID#0，驱动只接受 PCIE-1756,BID#0
            if (connection.StartsWith("PCIE://", StringComparison.OrdinalIgnoreCase)
                || connection.StartsWith("PCI://", StringComparison.OrdinalIgnoreCase)
                || connection.StartsWith("BDaq://", StringComparison.OrdinalIgnoreCase))
            {
                var schemeLen = connection.IndexOf("://", StringComparison.Ordinal);
                var body = schemeLen >= 0 ? connection[(schemeLen + 3)..] : connection;
                var deviceName = body.Split(';')[0].Trim();
                return (ConnectionType.Network, deviceName);
            }

            // NI-DAQmx：UI 存 NIDAQ://Dev1，驱动只要 Dev1
            if (connection.StartsWith("NIDAQ://", StringComparison.OrdinalIgnoreCase)
                || connection.StartsWith("NI-DAQ://", StringComparison.OrdinalIgnoreCase)
                || connection.StartsWith("NI://", StringComparison.OrdinalIgnoreCase)
                || connection.StartsWith("DAQ://", StringComparison.OrdinalIgnoreCase))
            {
                var schemeLen = connection.IndexOf("://", StringComparison.Ordinal);
                var body = schemeLen >= 0 ? connection[(schemeLen + 3)..] : connection;
                var deviceName = body.Split(';')[0].Trim();
                return (ConnectionType.Network, deviceName);
            }

            return (ConnectionType.Network, connection);
        }

        private static string ExtractValue(string[] parts, string key, string defaultValue)
        {
            foreach (var part in parts)
            {
                var kv = part.Split(new[] { '=' }, 2);
                if (kv.Length == 2 && kv[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    return kv[1].Trim();
            }

            return defaultValue;
        }
    }
}
