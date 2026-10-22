using System;
using System.Collections.Generic;
using System.Globalization;
using CoreShared;
using CoreShared.Events;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Attributes;
using DeviceCommunication.Models;

namespace DeviceCommunication.Protocols.Mock
{
    /// <summary>
    /// 无硬件内存 PLC。地址字符串不解析，仅作字典键。
    /// </summary>
    [ControllerDriver("Mock", "Memory", ControllerType.PLC)]
    public sealed class MockController : IController
    {
        private readonly Dictionary<string, object> _store =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new();
        private MockConfiguration? _configuration;
        private bool _connected;

        public MockController(ControllerConfiguration configuration)
        {
            if (configuration is null)
                throw new ArgumentNullException(nameof(configuration));

            _configuration = new MockConfiguration
            {
                Description = configuration.Description
            };
        }

        public bool IsConnected => _connected;

        public event EventHandler<ExceptionEvent>? OnException;
        public event EventHandler<ExecuteLogEvent>? OnExecuteLog;

        /// <summary>单点写入后触发；不进入 <see cref="IController"/>。</summary>
        public event EventHandler<MockAddressWrittenEventArgs>? Written;

        public IConfiguration? CreateConfiguration() => _configuration;

        public bool Initialize<T>(T parameter)
        {
            if (parameter is MockConfiguration config)
            {
                _configuration = config;
                WriteLog("MockController initialized.");
                return true;
            }

            if (parameter is ControllerConfiguration)
            {
                WriteLog("MockController initialized from ControllerConfiguration.");
                return true;
            }

            return _configuration is not null;
        }

        public bool Connect(int timeout)
        {
            _ = timeout;
            _connected = true;
            WriteLog("MockController connected (in-memory).");
            return true;
        }

        public void Disconnect()
        {
            _connected = false;
            WriteLog("MockController disconnected.");
        }

        public (bool success, object value) ReadValue(ControllerAddress? address)
        {
            if (!_connected)
                return (false, null!);

            var key = ResolveKey(address);
            if (key is null || address is null)
                return (false, null!);

            try
            {
                object raw;
                lock (_sync)
                {
                    if (!_store.TryGetValue(key, out var stored) || stored is null)
                    {
                        raw = DefaultFor(address.DataType);
                        _store[key] = raw;
                    }
                    else
                    {
                        raw = stored;
                    }
                }

                var value = address.TransformReadValue(raw) ?? raw;
                return (true, value);
            }
            catch (Exception ex)
            {
                RaiseException(nameof(ReadValue), ex);
                return (false, null!);
            }
        }

        public bool WriteValue<T>(ControllerAddress? address, T value)
        {
            if (!_connected)
                return false;

            var key = ResolveKey(address);
            if (key is null || address is null)
                return false;

            try
            {
                var coerced = Coerce(value, address.DataType);
                lock (_sync)
                    _store[key] = coerced;

                address.Value = coerced;
                Written?.Invoke(this, new MockAddressWrittenEventArgs(address, coerced, key));
                return true;
            }
            catch (Exception ex)
            {
                RaiseException(nameof(WriteValue), ex);
                return false;
            }
        }

        public (bool success, object[] values) ReadValues<T>(ControllerAddress[]? addresses, ushort length)
        {
            if (!_connected || addresses is null || addresses.Length == 0)
                return (false, Array.Empty<object>());

            var count = Math.Min(length > 0 ? length : (ushort)addresses.Length, addresses.Length);
            var values = new object[count];
            for (var i = 0; i < count; i++)
            {
                var (ok, value) = ReadValue(addresses[i]);
                if (!ok)
                    return (false, Array.Empty<object>());
                values[i] = value;
            }

            return (true, values);
        }

        public bool WriteValues<T>(ControllerAddress[]? addresses, T[]? values)
        {
            if (!_connected || addresses is null || values is null || addresses.Length == 0)
                return false;

            var count = Math.Min(addresses.Length, values.Length);
            for (var i = 0; i < count; i++)
            {
                if (!WriteValue(addresses[i], values[i]))
                    return false;
            }

            return true;
        }

        private static string? ResolveKey(ControllerAddress? address)
        {
            if (address is null)
                return null;

            if (!string.IsNullOrWhiteSpace(address.Address))
                return address.Address.Trim();

            if (!string.IsNullOrWhiteSpace(address.Name))
                return address.Name.Trim();

            return null;
        }

        private static object DefaultFor(DataConvert.DataType dataType) => dataType switch
        {
            DataConvert.DataType.Bool => false,
            DataConvert.DataType.Byte => (byte)0,
            DataConvert.DataType.UInt16 => (ushort)0,
            DataConvert.DataType.Int16 => (short)0,
            DataConvert.DataType.UInt32 => 0u,
            DataConvert.DataType.Int32 => 0,
            DataConvert.DataType.Float => 0f,
            DataConvert.DataType.Double => 0d,
            DataConvert.DataType.ULong => 0UL,
            DataConvert.DataType.Long => 0L,
            DataConvert.DataType.String => string.Empty,
            _ => 0
        };

        private static object Coerce<T>(T value, DataConvert.DataType dataType)
        {
            if (value is null)
                return DefaultFor(dataType);

            if (dataType == DataConvert.DataType.String)
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

            if (dataType == DataConvert.DataType.Bool)
            {
                if (value is bool b)
                    return b;
                if (value is string s && bool.TryParse(s, out var parsed))
                    return parsed;
                return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            }

            var targetType = DataConvert.ConvertType(dataType);
            if (value.GetType() == targetType)
                return value;

            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }

        private void WriteLog(string message)
        {
            OnExecuteLog?.Invoke(this, new ExecuteLogEvent
            {
                ClassName = nameof(MockController),
                MethodName = nameof(WriteLog),
                LogMessage = message
            });
        }

        private void RaiseException(string method, Exception ex)
        {
            OnException?.Invoke(this, new ExceptionEvent
            {
                ClassName = nameof(MockController),
                MethodName = method,
                ErrorMessage = ex.Message
            });
        }
    }
}
