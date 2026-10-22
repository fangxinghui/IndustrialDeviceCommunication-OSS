using System;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Infrastructure;
using DeviceCommunication.Models;

namespace DeviceCommunication.Protocols
{
    /// <summary>
    /// Modbus 连接参数解析与协议控制器创建，供各 Modbus 驱动在 CreateConfiguration 内复用。
    /// </summary>
    internal static class ModbusConfigurationBuilder
    {
        public static bool TryBuild(
            ControllerConfiguration configuration,
            out IConfiguration deviceConfiguration)
        {
            deviceConfiguration = null!;

            if (string.IsNullOrWhiteSpace(configuration.Parameters))
                return false;

            var parts = ControllerParameterParser.Split(configuration);
            if (configuration.ConnectionType == ConnectionType.Network)
                return TryBuildTcp(parts, out deviceConfiguration);

            if (configuration.ConnectionType == ConnectionType.SerialPort)
                return TryBuildRtu(parts, out deviceConfiguration);

            return false;
        }

        public static IController? CreateProtocolController(ControllerConfiguration configuration)
            => ModbusBusManager.Acquire(configuration);

        /// <summary>物理总线键（不含 SlaveId）。</summary>
        internal static string GetBusKey(ControllerConfiguration configuration)
        {
            var parts = ControllerParameterParser.Split(configuration);
            if (configuration.ConnectionType == ConnectionType.SerialPort && parts.Length >= 7)
            {
                return string.Join('|',
                    "RTU",
                    ControllerParameterParser.NormalizeSerialPortName(parts[0]),
                    parts[1], parts[2], parts[3], parts[4], parts[6]);
            }

            if (configuration.ConnectionType == ConnectionType.Network && parts.Length >= 4)
                return string.Join('|', "TCP", parts[0], parts[1], parts[3]);

            return configuration.Parameters ?? string.Empty;
        }

        internal static bool TryGetSlaveId(ControllerConfiguration configuration, out byte slaveId)
        {
            slaveId = 1;
            if (!TryBuild(configuration, out var deviceConfiguration))
                return false;

            switch (deviceConfiguration)
            {
                case ModbusRtuConfiguration rtu:
                    slaveId = rtu.StationId;
                    return true;
                case ModbusTcpConfiguration tcp:
                    slaveId = tcp.StationId;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryBuildTcp(string[] parts, out IConfiguration deviceConfiguration)
        {
            deviceConfiguration = null!;

            if (parts.Length < 4
                || !int.TryParse(parts[1], out var port)
                || !byte.TryParse(parts[2], out var slaveId)
                || !Enum.TryParse(parts[3], out ModbusDataType dataType))
            {
                return false;
            }

            deviceConfiguration = new ModbusTcpConfiguration
            {
                IpAddress = parts[0],
                Port = port,
                StationId = slaveId,
                ModbusDataType = dataType
            };
            return true;
        }

        private static bool TryBuildRtu(string[] parts, out IConfiguration deviceConfiguration)
        {
            deviceConfiguration = null!;

            if (parts.Length < 7
                || !int.TryParse(parts[1], out var baudRate)
                || !int.TryParse(parts[2], out var dataBits)
                || !ControllerParameterParser.TryParseStopBits(parts[3], out var stopBits)
                || !ControllerParameterParser.TryParseParity(parts[4], out var parity)
                || !byte.TryParse(parts[5], out var slaveId)
                || !Enum.TryParse(parts[6], out ModbusDataType dataType))
            {
                return false;
            }

            deviceConfiguration = new ModbusRtuConfiguration
            {
                PortName = ControllerParameterParser.NormalizeSerialPortName(parts[0]),
                BaudRate = baudRate,
                DataBits = dataBits,
                StopBits = stopBits,
                Parity = parity,
                StationId = slaveId,
                ModbusDataType = dataType
            };
            return true;
        }
    }
}
