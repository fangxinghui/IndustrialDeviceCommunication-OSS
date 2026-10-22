using System;
using System.Collections.Generic;
using System.Linq;
using CoreShared;
using CoreShared.Events;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Attributes;
using DeviceCommunication.Infrastructure;
using DeviceCommunication.Models;
using DeviceCommunication.Models.BeckoffConfig;
using DeviceCommunication.Protocols.BeckOff;
using DeviceCommunication.Protocols.Siemens;

namespace DeviceCommunication.Protocols.PLC
{
    internal abstract class ReflectionPlcDriverBase : IController, IModbusBusPollScope, IModbusSharedBusIdentity
    {
        protected readonly ControllerConfiguration SourceConfiguration;
        private readonly IController? _deviceController;

        protected ReflectionPlcDriverBase(ControllerConfiguration configuration)
        {
            SourceConfiguration = configuration;

            var deviceConfiguration = CreateConfiguration();
            if (deviceConfiguration is not null
                && TryCreateDevice(configuration, out var deviceController))
            {
                _deviceController = deviceController;
                WireControllerEvents(_deviceController);
                _deviceController.Initialize(deviceConfiguration);
            }
        }

        public IConfiguration? CreateConfiguration()
            => TryCreateDeviceConfiguration(SourceConfiguration, out var configuration) ? configuration : null;

        public bool IsConnected => _deviceController?.IsConnected ?? false;

        public string LogicalName =>
            ModbusBusControllerFacade.GetLogicalName(_deviceController, SourceConfiguration.Description);

        public byte SlaveId => ModbusBusControllerFacade.GetSlaveId(_deviceController);

        public string BusKey => ModbusBusControllerFacade.GetBusKey(_deviceController);

        public IDisposable EnterPollScope() => ModbusBusControllerFacade.EnterPollScope(_deviceController);

        public event EventHandler<ExceptionEvent>? OnException;
        public event EventHandler<ExecuteLogEvent>? OnExecuteLog;

        public bool Initialize<T>(T parameter) => _deviceController?.Initialize(parameter) ?? false;

        public bool Connect(int timeout) => _deviceController?.Connect(timeout) ?? false;

        public void Disconnect() => _deviceController?.Disconnect();

        public (bool success, object value) ReadValue(ControllerAddress? address)
            => _deviceController?.ReadValue(address) ?? (false, null!);

        public bool WriteValue<T>(ControllerAddress? address, T value)
            => _deviceController?.WriteValue(address, value) ?? false;

        public (bool success, object[] values) ReadValues<T>(ControllerAddress[]? addresses, ushort length)
            => _deviceController?.ReadValues<T>(addresses, length) ?? (false, Array.Empty<object>());

        public bool WriteValues<T>(ControllerAddress[]? addresses, T[]? values)
            => _deviceController?.WriteValues(addresses, values) ?? false;

        protected abstract bool TryCreateDeviceConfiguration(
            ControllerConfiguration configuration,
            out IConfiguration deviceConfiguration);

        protected abstract bool TryCreateDevice(
            ControllerConfiguration configuration,
            out IController deviceController);

        private void WireControllerEvents(IController controller)
        {
            controller.OnException += (_, e) => OnException?.Invoke(this, e);
            controller.OnExecuteLog += (_, e) => OnExecuteLog?.Invoke(this, e);
        }
    }

    [ControllerDriver("Siemens", "S7-1200", ControllerType.PLC)]
    [ControllerDriver("Siemens", "S7-1500", ControllerType.PLC)]
    [ControllerDriver("Siemens", "S7-300", ControllerType.PLC)]
    [ControllerDriver("Siemens", "S7-400", ControllerType.PLC)]
    [ControllerDriver("Siemens", "S7-200Smart", ControllerType.PLC)]
    [ControllerDriver("Siemens", "S7-200", ControllerType.PLC)]
    internal sealed class SiemensPlcController : ReflectionPlcDriverBase
    {
        public SiemensPlcController(ControllerConfiguration configuration) : base(configuration)
        {
        }

        protected override bool TryCreateDeviceConfiguration(
            ControllerConfiguration configuration,
            out IConfiguration deviceConfiguration)
        {
            deviceConfiguration = null!;

            var parts = ControllerParameterParser.Split(configuration);
            if (parts.Length < 2)
                return false;

            var plcType = configuration.Model switch
            {
                string m when m.Contains("1200", StringComparison.OrdinalIgnoreCase) => SiemensPLCType.S1200,
                string m when m.Contains("1500", StringComparison.OrdinalIgnoreCase) => SiemensPLCType.S1500,
                string m when m.Contains("300", StringComparison.OrdinalIgnoreCase) => SiemensPLCType.S300,
                string m when m.Contains("400", StringComparison.OrdinalIgnoreCase) => SiemensPLCType.S400,
                string m when m.Contains("200smart", StringComparison.OrdinalIgnoreCase) => SiemensPLCType.S200Smart,
                string m when m.Contains("200", StringComparison.OrdinalIgnoreCase) => SiemensPLCType.S200,
                _ => SiemensPLCType.S1200
            };

            deviceConfiguration = new SiemensNetConfiguration
            {
                IP = parts[0],
                Port = ControllerParameterParser.ParseInt(parts.ElementAtOrDefault(1), 102),
                Rack = byte.TryParse(parts.ElementAtOrDefault(2), out var rack) ? rack : (byte)0,
                Slot = byte.TryParse(parts.ElementAtOrDefault(3), out var slot) ? slot : (byte)2,
                PLCType = plcType
            };
            return true;
        }

        protected override bool TryCreateDevice(
            ControllerConfiguration configuration,
            out IController deviceController)
        {
            deviceController = new SiemensNet();
            return true;
        }
    }

    [ControllerDriver("Beckhoff", "*", ControllerType.PLC)]
    [ControllerDriver("BeckHoff", "*", ControllerType.PLC)]
    internal sealed class BeckhoffPlcController : ReflectionPlcDriverBase
    {
        public BeckhoffPlcController(ControllerConfiguration configuration) : base(configuration)
        {
        }

        protected override bool TryCreateDeviceConfiguration(
            ControllerConfiguration configuration,
            out IConfiguration deviceConfiguration)
        {
            deviceConfiguration = null!;
            return TryCreateBeckhoffConfiguration(configuration, out deviceConfiguration);
        }

        protected override bool TryCreateDevice(
            ControllerConfiguration configuration,
            out IController deviceController)
            => TryCreateBeckhoffDevice(configuration, out deviceController);

        private static bool TryCreateBeckhoffConfiguration(
            ControllerConfiguration configuration,
            out IConfiguration deviceConfiguration)
        {
            deviceConfiguration = null!;
            return TryCreateBeckhoff(configuration, out _, out deviceConfiguration);
        }

        private static bool TryCreateBeckhoffDevice(
            ControllerConfiguration configuration,
            out IController deviceController)
        {
            deviceController = null!;
            return TryCreateBeckhoff(configuration, out deviceController, out _);
        }

        private static bool TryCreateBeckhoff(
            ControllerConfiguration configuration,
            out IController deviceController,
            out IConfiguration deviceConfiguration)
        {
            deviceController = null!;
            deviceConfiguration = null!;

            var parts = ControllerParameterParser.Split(configuration);
            var values = ControllerParameterParser.ParseKeyValues(parts);
            var mode = ControllerParameterParser.GetValue(values, "AdsMode");

            if (string.Equals(mode, "TcpRouter", StringComparison.OrdinalIgnoreCase))
                return TryCreateTcpRouter(parts, values, out deviceController, out deviceConfiguration);

            if (string.Equals(mode, "TwinCAT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mode, "TwinCat", StringComparison.OrdinalIgnoreCase))
                return TryCreateTwinCat(parts, values, out deviceController, out deviceConfiguration);

            if (parts.Length > 0 && parts[0] == "1")
                return TryCreateLegacyTwinCat(parts, out deviceController, out deviceConfiguration);

            if (parts.Length >= 6)
                return TryCreateLegacyTcpRouter(parts, out deviceController, out deviceConfiguration);

            return TryCreateTwinCat(parts, values, out deviceController, out deviceConfiguration);
        }

        private static bool TryCreateTwinCat(
            string[] parts,
            IReadOnlyDictionary<string, string> values,
            out IController deviceController,
            out IConfiguration deviceConfiguration)
        {
            deviceController = null!;
            deviceConfiguration = null!;

            var remoteNetId = ControllerParameterParser.GetValue(values, "RemoteNetId");
            var port = ControllerParameterParser.ParseInt(ControllerParameterParser.GetValue(values, "Port"), 851);

            if (string.IsNullOrWhiteSpace(remoteNetId) && parts.Length > 0)
            {
                var netIdPort = parts[0].Split(':');
                remoteNetId = netIdPort.ElementAtOrDefault(0);
                port = ControllerParameterParser.ParseInt(netIdPort.ElementAtOrDefault(1), port);
            }

            if (string.IsNullOrWhiteSpace(remoteNetId))
                return false;

            deviceController = new BaseAdsClient();
            deviceConfiguration = new BaseTwincatConfiguration
            {
                RemoteNetId = remoteNetId,
                Port = port
            };
            return true;
        }

        private static bool TryCreateTcpRouter(
            string[] parts,
            IReadOnlyDictionary<string, string> values,
            out IController deviceController,
            out IConfiguration deviceConfiguration)
        {
            deviceController = null!;
            deviceConfiguration = null!;

            var remoteAddress = ControllerParameterParser.GetValue(values, "RemoteAddress") ?? parts.ElementAtOrDefault(0);
            var port = ControllerParameterParser.ParseInt(
                ControllerParameterParser.GetValue(values, "Port") ?? parts.ElementAtOrDefault(1), 851);
            var remoteNetId = ControllerParameterParser.GetValue(values, "RemoteNetId");
            var localAddress = ControllerParameterParser.GetValue(values, "LocalAddress");
            var localNetId = ControllerParameterParser.GetValue(values, "LocalNetId");
            var routerName = ControllerParameterParser.GetValue(values, "RouterName");

            if (string.IsNullOrWhiteSpace(remoteAddress)
                || string.IsNullOrWhiteSpace(remoteNetId)
                || string.IsNullOrWhiteSpace(localAddress)
                || string.IsNullOrWhiteSpace(localNetId)
                || string.IsNullOrWhiteSpace(routerName))
            {
                return false;
            }

            deviceController = new BaseAdsClientNoTwinCat();
            deviceConfiguration = new BaseConfiguration
            {
                RemoteAddress = remoteAddress,
                Port = port,
                RemoteNetId = remoteNetId,
                LocalAddress = localAddress,
                LocalNetId = localNetId,
                RouterName = routerName
            };
            return true;
        }

        private static bool TryCreateLegacyTwinCat(
            string[] parts,
            out IController deviceController,
            out IConfiguration deviceConfiguration)
        {
            deviceController = null!;
            deviceConfiguration = null!;

            var netIdPort = parts.ElementAtOrDefault(1)?.Split(':') ?? Array.Empty<string>();
            var remoteNetId = netIdPort.ElementAtOrDefault(0);
            if (string.IsNullOrWhiteSpace(remoteNetId))
                return false;

            deviceController = new BaseAdsClient();
            deviceConfiguration = new BaseTwincatConfiguration
            {
                RemoteNetId = remoteNetId,
                Port = ControllerParameterParser.ParseInt(netIdPort.ElementAtOrDefault(1), 851)
            };
            return true;
        }

        private static bool TryCreateLegacyTcpRouter(
            string[] parts,
            out IController deviceController,
            out IConfiguration deviceConfiguration)
        {
            deviceController = null!;
            deviceConfiguration = null!;

            var remote = parts[1].Split(':');
            var remoteAddress = remote.ElementAtOrDefault(0);
            if (string.IsNullOrWhiteSpace(remoteAddress))
                return false;

            deviceController = new BaseAdsClientNoTwinCat();
            deviceConfiguration = new BaseConfiguration
            {
                RemoteAddress = remoteAddress,
                Port = ControllerParameterParser.ParseInt(remote.ElementAtOrDefault(1), 851),
                RemoteNetId = parts.ElementAtOrDefault(2),
                LocalAddress = parts.ElementAtOrDefault(3),
                LocalNetId = parts.ElementAtOrDefault(4),
                RouterName = parts.ElementAtOrDefault(5)
            };
            return true;
        }
    }

    [ControllerDriver("Modbus", "*", ControllerType.PLC, IsDefault = true)]
    internal sealed class ModbusPlcController : ReflectionPlcDriverBase
    {
        public ModbusPlcController(ControllerConfiguration configuration) : base(configuration)
        {
        }

        protected override bool TryCreateDeviceConfiguration(
            ControllerConfiguration configuration,
            out IConfiguration deviceConfiguration)
            => ModbusConfigurationBuilder.TryBuild(configuration, out deviceConfiguration);

        protected override bool TryCreateDevice(
            ControllerConfiguration configuration,
            out IController deviceController)
        {
            deviceController = ModbusConfigurationBuilder.CreateProtocolController(configuration)!;
            return deviceController is not null;
        }
    }
}
