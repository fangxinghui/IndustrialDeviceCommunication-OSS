using System;
using CoreShared.Events;
using CoreShared;

namespace DeviceCommunication.Abstractions
{
    public interface IDigitalIo
    {
        event EventHandler<ExceptionEvent>? OnException;
        event EventHandler<ExecuteLogEvent>? OnExecuteLog;
        bool ReadDigitalInput(ControllerAddress channel, out bool value);
        bool ReadAllDigitalInputs(ControllerAddress[] startRegister,int count,out bool[] values);
        bool WriteDigitalOutput(ControllerAddress channel, bool value);
        bool WriteAllDigitalOutputs(ControllerAddress[] startRegister, bool[] values);
    }
}