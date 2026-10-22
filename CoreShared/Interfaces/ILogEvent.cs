using CoreShared.Events;
using System;

namespace CoreShared.Interfaces
{
    public interface ILogEvent
    {
        public event EventHandler<ExceptionEvent>? OnException;
        public event EventHandler<ExecuteLogEvent>? OnExecuteLog;
    }
}
