using System;
using System;
using System.Threading;
using CoreShared;
using CoreShared.Events;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Infrastructure;
using DeviceCommunication.Models;

namespace DeviceCommunication.Protocols
{
    /// <summary>
    /// 绑定到共享Modbus 总线上的单个从站视图。
    /// </summary>
    internal sealed class ModbusSlaveController :
        IController,
        IModbusBusLease,
        IModbusBusPollScope,
        IModbusSharedBusIdentity
    {
        private readonly ModbusBusEntry _entry;
        private readonly byte _slaveId;
        private readonly string _logicalName;
        private bool _released;

        internal ModbusSlaveController(ModbusBusEntry entry, byte slaveId, string logicalName)
        {
            _entry = entry;
            _slaveId = slaveId;
            _logicalName = logicalName;
            BusKey = entry.BusKey;
        }

        internal ModbusBusEntry Entry => _entry;

        public string BusKey { get; }
        public string LogicalName => _logicalName;
        public byte SlaveId => _slaveId;

        public bool IsConnected
        {
            get
            {
                lock (_entry.Sync)
                {
                    if (_released)
                        return false;

                    if (_entry.Transport.IsConnected)
                        _entry.IsLinkConnected = true;

                    return _entry.IsLinkConnected;
                }
            }
        }

        public event EventHandler<ExceptionEvent>? OnException;
        public event EventHandler<ExecuteLogEvent>? OnExecuteLog;

        public IConfiguration? CreateConfiguration() => null;

        public bool Initialize<T>(T parameter)
        {
            lock (_entry.Sync)
            {
                WireTransportEventsOnce();

                if (_entry.Initialized)
                {
                    LogExecute(
                        nameof(Initialize),
                        $"[{LogicalName}] joined shared bus {BusKey} (Slave={SlaveId}, peers: {_entry.DescribeLeases()})");
                    return true;
                }

                object initParameter = parameter switch
                {
                    ModbusRtuConfiguration rtu => rtu,
                    ModbusTcpConfiguration tcp => new ModbusTcpConfiguration
                    {
                        IpAddress = tcp.IpAddress,
                        Port = tcp.Port,
                        ModbusDataType = tcp.ModbusDataType,
                        StationId = 1
                    },
                    _ => parameter!
                };

                _entry.Initialized = _entry.Transport.Initialize(initParameter);
                if (_entry.Initialized)
                {
                    LogExecute(
                        nameof(Initialize),
                        $"[{LogicalName}] opened shared bus {BusKey} (Slave={SlaveId}, first owner)");
                }

                return _entry.Initialized;
            }
        }

        public bool Connect(int timeout)
        {
            lock (_entry.Sync)
            {
                if (_released)
                    return false;

                if (_entry.Transport.IsConnected)
                {
                    _entry.IsLinkConnected = true;
                    LogExecute(
                        nameof(Connect),
                        $"[{LogicalName}] using existing connection on {BusKey} (Slave={SlaveId})");
                    return true;
                }

                if (!_entry.Initialized)
                    return false;

                var connected = _entry.Transport.Connect(timeout);
                if (connected)
                {
                    _entry.IsLinkConnected = true;
                    LogExecute(
                        nameof(Connect),
                        $"[{LogicalName}] physical link connected on {BusKey} (Slave={SlaveId})");
                }

                return connected;
            }
        }

        public void Disconnect() => ReleaseBus();

        public void ReleaseBus()
        {
            // 必须在 _entry.Sync 锁内设置 _released，与 ExecuteOnTransport 中的读取-检查保持同步，
            // 防止竞态：另一个线程正在 ExecuteOnTransport 中检查并越过 _released == false 的同时，
            // 本线程释放了总线（断开传输层连接）
            lock (_entry.Sync)
            {
                if (_released)
                    return;

                _released = true;
            }

            LogExecute(nameof(ReleaseBus), $"[{LogicalName}] releasing bus lease on {BusKey} (Slave={SlaveId})");
            ModbusBusManager.Release(this);
        }

        public IDisposable EnterPollScope() => new PollScope(this);

        public (bool success, object value) ReadValue(ControllerAddress? address)
            => ExecuteOnTransport(c => c.ReadValue(address));

        public bool WriteValue<T>(ControllerAddress? address, T value)
            => ExecuteOnTransport(c => c.WriteValue(address, value));

        public (bool success, object[] values) ReadValues<T>(ControllerAddress[]? addresses, ushort length)
            => ExecuteOnTransport(c => c.ReadValues<T>(addresses, length));

        public bool WriteValues<T>(ControllerAddress[]? addresses, T[]? values)
            => ExecuteOnTransport(c => c.WriteValues(addresses, values));

        private T ExecuteOnTransport<T>(Func<IController, T> action)
        {
            lock (_entry.Sync)
            {
                if (_released)
                    throw new InvalidOperationException($"Modbus bus lease released: {LogicalName}");

                ApplySlaveId();
                return action(_entry.Transport);
            }
        }

        private void ApplySlaveId()
        {
            if (_entry.Transport is IModbus modbus)
                modbus.StationId = _slaveId;
        }

        private void WireTransportEventsOnce()
        {
            if (_entry.EventsWired)
                return;

            _entry.Transport.OnException += (_, e) => OnException?.Invoke(this, e);
            _entry.Transport.OnExecuteLog += (_, e) => OnExecuteLog?.Invoke(this, e);
            _entry.EventsWired = true;
        }

        private void LogExecute(string method, string message)
        {
            OnExecuteLog?.Invoke(this, new ExecuteLogEvent
            {
                MethodName = method,
                ClassName = nameof(ModbusSlaveController),
                LogMessage = message
            });
        }

        private sealed class PollScope : IDisposable
        {
            private readonly ModbusSlaveController _owner;
            private bool _disposed;

            public PollScope(ModbusSlaveController owner)
            {
                _owner = owner;
                Monitor.Enter(_owner._entry.Sync);
                if (_owner._released)
                {
                    Monitor.Exit(_owner._entry.Sync);
                    throw new InvalidOperationException($"Modbus bus lease released: {_owner.LogicalName}");
                }

                _owner.ApplySlaveId();
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                Monitor.Exit(_owner._entry.Sync);
            }
        }
    }
}

