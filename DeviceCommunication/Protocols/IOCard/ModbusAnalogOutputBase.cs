using System;
using System.Collections.Generic;
using System.Globalization;
using CoreShared;
using CoreShared.Events;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Models;
using DeviceCommunication.Protocols;
using IConfiguration = DeviceCommunication.Abstractions.IConfiguration;

namespace DeviceCommunication.Protocols.IOCard;

public abstract class ModbusAnalogOutputBase : IAnalogOutputIo, IController, IModbusBusPollScope, IModbusSharedBusIdentity
{
    private readonly ControllerConfiguration _sourceConfiguration;

    public event EventHandler<ExceptionEvent>? OnException;
    public event EventHandler<ExecuteLogEvent>? OnExecuteLog;

    protected virtual IController? Controller { get; private set; }

    protected abstract int ChannelCount { get; }
    protected abstract int StartRegister { get; }
    protected virtual int ChannelRegisterStride => 1;

    public bool IsConnected => Controller?.IsConnected ?? false;

    public string LogicalName =>
        ModbusBusControllerFacade.GetLogicalName(Controller, _sourceConfiguration.Description);

    public byte SlaveId => ModbusBusControllerFacade.GetSlaveId(Controller);

    public string BusKey => ModbusBusControllerFacade.GetBusKey(Controller);

    public IDisposable EnterPollScope() => ModbusBusControllerFacade.EnterPollScope(Controller);

    protected ModbusAnalogOutputBase(ControllerConfiguration controllerConfiguration)
    {
        _sourceConfiguration = controllerConfiguration;
        try
        {
            Controller = ModbusConfigurationBuilder.CreateProtocolController(controllerConfiguration);
            WireControllerEvents(Controller);
        }
        catch (Exception ex)
        {
            OnException?.Invoke(this, new ExceptionEvent
            {
                MethodName = nameof(ModbusAnalogOutputBase),
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
    public (bool success, object value) ReadValue(ControllerAddress? address) => Controller?.ReadValue(address) ?? (false, null!);
    public bool WriteValue<T>(ControllerAddress? address, T value) => Controller?.WriteValue(address, value) ?? false;
    public (bool success, object[] values) ReadValues<T>(ControllerAddress[]? addresses, ushort length)
        => Controller?.ReadValues<T>(addresses, length) ?? (false, Array.Empty<object>());
    public bool WriteValues<T>(ControllerAddress[]? addresses, T[]? values) => Controller?.WriteValues(addresses, values) ?? false;

    public virtual bool WriteAnalogOutput(ControllerAddress channel, double signalValue)
    {
        if (!TryBuildAnalogOutputRaw(channel, signalValue, out var rawValue))
            return false;

        return WriteAnalogOutputRaw(channel, rawValue);
    }

    public virtual bool WriteAnalogOutputRaw(ControllerAddress channel, ushort rawValue)
    {
        if (!EnsureConnected(nameof(WriteAnalogOutputRaw)))
            return false;

        if (!TryResolveChannelIndex(channel, out var channelIndex))
        {
            WriteLog(nameof(WriteAnalogOutputRaw), "Invalid analog output channel.");
            return false;
        }

        var address = BuildHoldingRegisterAddress(GetChannelRegisterIndex(channelIndex));
        return Controller!.WriteValue(new ControllerAddress { Address = address }, rawValue);
    }

    public virtual bool TryBuildAnalogOutputRaw(ControllerAddress channel, double signalValue, out ushort rawValue)
    {
        rawValue = 0;
        var options = ResolveChannelOptions(channel);
        var raw = ConvertSignalToRaw(signalValue, options);
        if (raw < 0 || raw > ushort.MaxValue)
            return false;

        rawValue = (ushort)Math.Round(raw, MidpointRounding.AwayFromZero);
        return true;
    }

    protected virtual double ConvertSignalToRaw(double signalValue, AnalogOutputChannelOptions options)
    {
        return signalValue;
    }

    protected virtual AnalogOutputChannelOptions ResolveChannelOptions(ControllerAddress? channel)
    {
        var parameters = ParseAddressParameters(channel?.Address);
        return new AnalogOutputChannelOptions
        {
            OutputRange = GetFirst(parameters, "outputRange", "signalRange", "range", "scale"),
            Parameters = parameters
        };
    }

    protected virtual bool TryResolveChannelIndex(ControllerAddress channel, out int channelIndex)
    {
        channelIndex = -1;
        if (channel is null)
            return false;

        if (!string.IsNullOrWhiteSpace(channel.Address) &&
            TryParseHoldingRegisterAddress(channel.Address, out var register))
        {
            var offset = register - StartRegister;
            if (offset < 0 || offset % ChannelRegisterStride != 0)
                return false;

            channelIndex = offset / ChannelRegisterStride;
            return channelIndex < ChannelCount;
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

    protected static string BuildHoldingRegisterAddress(int registerIndex) => $"x=6;{registerIndex}";

    protected virtual int GetChannelRegisterIndex(int channelIndex) =>
        StartRegister + channelIndex * ChannelRegisterStride;

    protected static bool TryParseHoldingRegisterAddress(string? address, out int register)
    {
        register = 0;
        if (string.IsNullOrWhiteSpace(address))
            return false;

        var parts = address.Split(';');
        if (parts.Length < 2 || !parts[0].Equals("x=6", StringComparison.OrdinalIgnoreCase))
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

    private void WireControllerEvents(IController? controller)
    {
        if (controller is null) return;
        controller.OnException += (_, e) => OnException?.Invoke(this, e);
        controller.OnExecuteLog += (_, e) => OnExecuteLog?.Invoke(this, e);
    }
}
