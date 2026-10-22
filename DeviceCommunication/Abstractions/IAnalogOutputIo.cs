using System;
using CoreShared;
using CoreShared.Events;
using DeviceCommunication.Models;

namespace DeviceCommunication.Abstractions;

public interface IAnalogOutputIo
{
    event EventHandler<ExceptionEvent>? OnException;
    event EventHandler<ExecuteLogEvent>? OnExecuteLog;

    bool WriteAnalogOutput(ControllerAddress channel, double signalValue);
    bool WriteAnalogOutputRaw(ControllerAddress channel, ushort rawValue);
    bool TryBuildAnalogOutputRaw(ControllerAddress channel, double signalValue, out ushort rawValue);
}
