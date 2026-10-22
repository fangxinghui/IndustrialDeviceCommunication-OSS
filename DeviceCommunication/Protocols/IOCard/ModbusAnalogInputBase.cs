using System;
using System.Collections.Generic;
using System.Globalization;
using CoreShared;
using CoreShared.Events;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Models;
using DeviceCommunication.Protocols;
using IConfiguration = DeviceCommunication.Abstractions.IConfiguration;

namespace DeviceCommunication.Protocols.IOCard
{
    /// <summary>
    /// Modbus 模拟量输入基类：通过功能码 0x04 读输入寄存器并换算为工程值。
    /// </summary>
    public abstract class ModbusAnalogInputBase : IAnalogIo, IController, IModbusBusPollScope, IModbusSharedBusIdentity
    {
        private readonly ControllerConfiguration _sourceConfiguration;

        public event EventHandler<ExceptionEvent>? OnException;
        public event EventHandler<ExecuteLogEvent>? OnExecuteLog;

        protected virtual IController? Controller { get; private set; }

        protected abstract int ChannelCount { get; }
        protected abstract int StartRegister { get; }
        protected abstract AnalogInputScaleMode DefaultScaleMode { get; }

        public AnalogInputScaleMode ScaleMode { get; set; }

        public bool IsConnected => Controller?.IsConnected ?? false;

        public string LogicalName =>
            ModbusBusControllerFacade.GetLogicalName(Controller, _sourceConfiguration.Description);

        public byte SlaveId => ModbusBusControllerFacade.GetSlaveId(Controller);

        public string BusKey => ModbusBusControllerFacade.GetBusKey(Controller);

        public IDisposable EnterPollScope() => ModbusBusControllerFacade.EnterPollScope(Controller);

        protected ModbusAnalogInputBase(ControllerConfiguration controllerConfiguration)
        {
            _sourceConfiguration = controllerConfiguration;
            try
            {
                Controller = ModbusConfigurationBuilder.CreateProtocolController(controllerConfiguration);
                ScaleMode = DefaultScaleMode;
                WireControllerEvents(Controller);
            }
            catch (Exception ex)
            {
                OnException?.Invoke(this, new ExceptionEvent
                {
                    MethodName = nameof(ModbusAnalogInputBase),
                    ClassName = GetType().Name,
                    ErrorMessage = ex.Message
                });
            }
        }

        public IConfiguration? CreateConfiguration()
            => ModbusConfigurationBuilder.TryBuild(_sourceConfiguration, out var configuration) ? configuration : null;

        public bool Initialize<T>(T parameter) => Controller?.Initialize(parameter) ?? false;

        public bool Connect(int timeout) => Controller?.Connect(timeout) ?? false;

        public void Disconnect() => Controller?.Disconnect();

        public (bool success, object value) ReadValue(ControllerAddress? address)
            => Controller?.ReadValue(address) ?? (false, null!);

        public bool WriteValue<T>(ControllerAddress? address, T value)
            => Controller?.WriteValue(address, value) ?? false;

        public (bool success, object[] values) ReadValues<T>(ControllerAddress[]? addresses, ushort length)
            => Controller?.ReadValues<T>(addresses, length) ?? (false, Array.Empty<object>());

        public bool WriteValues<T>(ControllerAddress[]? addresses, T[]? values)
            => Controller?.WriteValues(addresses, values) ?? false;

        private void WireControllerEvents(IController? controller)
        {
            if (controller is null) return;
            controller.OnException += (_, e) => OnException?.Invoke(this, e);
            controller.OnExecuteLog += (_, e) => OnExecuteLog?.Invoke(this, e);
        }

        public virtual bool ReadAnalogInput(ControllerAddress channel, out double value)
        {
            value = 0;
            if (!ReadAnalogInputReading(channel, out var reading))
                return false;

            value = reading.Value;
            return true;
        }

        public virtual bool ReadAnalogInputReading(ControllerAddress channel, out AnalogInputReading reading)
        {
            reading = new AnalogInputReading();
            if (!EnsureConnected(nameof(ReadAnalogInput)))
                return false;

            if (!TryResolveChannelIndex(channel, out var channelIndex))
            {
                WriteLog(nameof(ReadAnalogInput), "Invalid analog input channel.");
                return false;
            }

            var address = BuildInputRegisterAddress(StartRegister + channelIndex);
            var result = Controller!.ReadValue(new ControllerAddress { Address = address });
            if (!result.success || !TryGetRawValue(result.value, out var raw))
                return false;

            var options = ResolveChannelOptions(channel);
            reading = BuildReading(raw, options);
            return true;
        }

        public virtual bool ReadAllAnalogInputs(ControllerAddress[] startRegister, int count, out double[] values)
        {
            values = Array.Empty<double>();
            if (!EnsureConnected(nameof(ReadAllAnalogInputs)))
                return false;

            var readCount = Math.Min(count, ChannelCount);
            if (readCount <= 0)
            {
                WriteLog(nameof(ReadAllAnalogInputs), "Read count must be greater than 0.");
                return false;
            }

            var start = ResolveBatchStartRegister(startRegister);
            var address = new ControllerAddress { Address = BuildInputRegisterAddress(start) };
            var result = Controller!.ReadValues<ushort>(new[] { address }, (ushort)readCount);
            if (!result.success || result.values is null || result.values.Length == 0)
                return false;

            values = new double[result.values.Length];
            for (var i = 0; i < result.values.Length; i++)
            {
                if (!TryGetRawValue(result.values[i], out var raw))
                    return false;

                var channel = ResolveBatchChannel(startRegister, i);
                values[i] = BuildReading(raw, ResolveChannelOptions(channel)).Value;
            }

            return true;
        }

        protected static string BuildInputRegisterAddress(int registerIndex) => $"x=4;{registerIndex}";

        protected virtual AnalogInputReading ConvertRawToSignal(ushort raw, AnalogInputChannelOptions options)
        {
            return new AnalogInputReading
            {
                RawValue = raw,
                SignalValue = raw
            };
        }

        protected virtual AnalogInputReading BuildReading(ushort raw, AnalogInputChannelOptions options)
        {
            var reading = ConvertRawToSignal(raw, options);
            reading.RawValue = raw;
            return reading;
        }

        protected virtual AnalogInputChannelOptions ResolveChannelOptions(ControllerAddress? channel)
        {
            var parameters = ParseAddressParameters(channel?.Address);
            return new AnalogInputChannelOptions
            {
                InputRange = GetFirst(parameters, "inputRange", "range", "scale"),
                Parameters = parameters
            };
        }

        protected virtual bool TryResolveChannelIndex(ControllerAddress channel, out int channelIndex)
        {
            channelIndex = -1;
            if (channel is null)
                return false;

            if (!string.IsNullOrWhiteSpace(channel.Address) &&
                TryParseInputRegisterAddress(channel.Address, out var register))
            {
                channelIndex = register - StartRegister;
                return channelIndex >= 0 && channelIndex < ChannelCount;
            }

            if (int.TryParse(channel.Channel, out var parsed))
            {
                channelIndex = parsed >= 1 ? parsed - 1 : parsed;
                return channelIndex >= 0 && channelIndex < ChannelCount;
            }

            if (int.TryParse(channel.Point, out parsed))
            {
                channelIndex = parsed >= 1 ? parsed - 1 : parsed;
                return channelIndex >= 0 && channelIndex < ChannelCount;
            }

            return false;
        }

        protected virtual ControllerAddress? ResolveBatchChannel(ControllerAddress[]? startRegister, int offset)
        {
            if (startRegister is null || startRegister.Length == 0)
                return null;

            if (offset < startRegister.Length)
                return startRegister[offset];

            return startRegister[0];
        }

        protected virtual int ResolveBatchStartRegister(ControllerAddress[]? startRegister)
        {
            if (startRegister != null && startRegister.Length > 0 &&
                TryResolveChannelIndex(startRegister[0], out var channelIndex))
            {
                return StartRegister + channelIndex;
            }

            return StartRegister;
        }

        protected static bool TryParseInputRegisterAddress(string? address, out int register)
        {
            register = 0;
            if (string.IsNullOrWhiteSpace(address))
                return false;

            var parts = address.Split(';');
            if (parts.Length < 2 || !parts[0].Equals("x=4", StringComparison.OrdinalIgnoreCase))
                return false;

            return int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out register);
        }

        protected static IReadOnlyDictionary<string, string> ParseAddressParameters(string? address)
        {
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(address))
                return parameters;

            var parts = address.Split(';');
            for (var i = 2; i < parts.Length; i++)
            {
                var part = parts[i];
                var separator = part.IndexOf('=');
                if (separator <= 0)
                    continue;

                var key = part.Substring(0, separator).Trim();
                var value = part.Substring(separator + 1).Trim();
                if (!string.IsNullOrWhiteSpace(key))
                    parameters[key] = value;
            }

            return parameters;
        }

        protected static string? GetFirst(IReadOnlyDictionary<string, string> parameters, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return null;
        }

        protected static bool TryGetRawValue(object? value, out ushort raw)
        {
            raw = 0;
            if (value is ushort ushortValue)
            {
                raw = ushortValue;
                return true;
            }

            if (value is short shortValue && shortValue >= 0)
            {
                raw = (ushort)shortValue;
                return true;
            }

            if (value is int intValue && intValue >= 0 && intValue <= ushort.MaxValue)
            {
                raw = (ushort)intValue;
                return true;
            }

            if (value is uint uintValue && uintValue <= ushort.MaxValue)
            {
                raw = (ushort)uintValue;
                return true;
            }

            return ushort.TryParse(value?.ToString(), out raw);
        }

        protected bool EnsureConnected(string methodName)
        {
            if (Controller is null)
            {
                WriteLog(methodName, "Controller is null.");
                return false;
            }

            if (!Controller.IsConnected)
            {
                WriteLog(methodName, "Controller is not connected.");
                return false;
            }

            return true;
        }

        protected void WriteLog(string methodName, string message)
        {
            OnExecuteLog?.Invoke(this, new ExecuteLogEvent
            {
                MethodName = methodName,
                ClassName = GetType().Name,
                LogMessage = message
            });
        }
    }
}
