using System;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Infrastructure;
using DeviceCommunication.Models;

namespace DeviceCommunication.Protocols
{
    /// <summary>
    /// 共享 Modbus 物理链路（同 COM / 同 TCP），多个从站通过 SlaveId 区分。
    /// </summary>
    internal static class ModbusBusManager
    {
        private static readonly object Lock = new();
        private static readonly Dictionary<string, ModbusBusEntry> Buses = new(StringComparer.OrdinalIgnoreCase);

        public static IController Acquire(ControllerConfiguration configuration)
        {
            if (!ModbusConfigurationBuilder.TryGetSlaveId(configuration, out var slaveId))
                slaveId = 1;

            var logicalName = ResolveLogicalName(configuration, slaveId);
            var busKey = ModbusConfigurationBuilder.GetBusKey(configuration);

            lock (Lock)
            {
                if (!Buses.TryGetValue(busKey, out var entry))
                {
                    entry = new ModbusBusEntry(busKey, CreateRawTransport(configuration));
                    Buses[busKey] = entry;
                }

                var controller = new ModbusSlaveController(entry, slaveId, logicalName);
                entry.RegisterLease(controller);
                entry.RefCount++;
                return controller;
            }
        }

        internal static void Release(ModbusSlaveController controller)
        {
            lock (Lock)
            {
                if (!Buses.TryGetValue(controller.BusKey, out var entry) || entry != controller.Entry)
                    return;

                entry.UnregisterLease(controller);
                entry.RefCount = Math.Max(0, entry.RefCount - 1);
                if (entry.RefCount > 0)
                    return;

                try { entry.Transport.Disconnect(); }
                catch { }

                entry.IsLinkConnected = false;
                Buses.Remove(controller.BusKey);
            }
        }

        private static string ResolveLogicalName(ControllerConfiguration configuration, byte slaveId)
        {
            if (!string.IsNullOrWhiteSpace(configuration.Description))
                return configuration.Description.Trim();

            if (!string.IsNullOrWhiteSpace(configuration.BrandName) && !string.IsNullOrWhiteSpace(configuration.Model))
                return $"{configuration.BrandName}-{configuration.Model}-Slave{slaveId}";

            return $"Slave-{slaveId}";
        }

        private static IController CreateRawTransport(ControllerConfiguration configuration)
        {
            if (configuration.ConnectionType == ConnectionType.Network)
                return new ModbusTcp();

            if (configuration.ConnectionType == ConnectionType.SerialPort)
                return new ModbusRtu();

            throw new NotSupportedException($"Unsupported Modbus connection type: {configuration.ConnectionType}");
        }
    }

    internal sealed class ModbusBusEntry
    {
        private readonly List<ModbusSlaveController> _leases = new();

        public ModbusBusEntry(string busKey, IController transport)
        {
            BusKey = busKey;
            Transport = transport;
        }

        public string BusKey { get; }
        public IController Transport { get; }
        public object Sync { get; } = new();
        public int RefCount { get; set; }
        public bool Initialized { get; set; }
        public bool EventsWired { get; set; }
        public bool IsLinkConnected { get; set; }

        internal void RegisterLease(ModbusSlaveController lease) => _leases.Add(lease);

        internal void UnregisterLease(ModbusSlaveController lease) => _leases.Remove(lease);

        internal string DescribeLeases()
        {
            if (_leases.Count == 0)
                return string.Empty;

            return string.Join(", ", _leases.Select(l => $"{l.LogicalName}(Slave={l.SlaveId})"));
        }
    }
}

