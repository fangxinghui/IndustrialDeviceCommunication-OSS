using System;
using CoreShared;
using CoreShared.Events;
using DeviceCommunication.Models;

namespace DeviceCommunication.Abstractions
{
    public interface IAnalogIo
    {
        event EventHandler<ExceptionEvent>? OnException;
        event EventHandler<ExecuteLogEvent>? OnExecuteLog;

        bool ReadAnalogInput(ControllerAddress channel, out double value);
        bool ReadAnalogInputReading(ControllerAddress channel, out AnalogInputReading reading);
        bool ReadAllAnalogInputs(ControllerAddress[] startRegister, int count, out double[] values);
    }

}
