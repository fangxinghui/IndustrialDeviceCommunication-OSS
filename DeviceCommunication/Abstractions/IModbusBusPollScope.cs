using System;

namespace DeviceCommunication.Abstractions
{
    /// <summary>
    /// 共享 Modbus 总线上的逻辑控制器：整轮轮询期间独占总线，避免中途被其他从站插队。
    /// </summary>
    public interface IModbusBusPollScope
    {
        /// <summary>
        /// 进入轮询会话：持有总线锁直到 Dispose，期间 StationId 固定为本从站。
        /// </summary>
        IDisposable EnterPollScope();
    }
}
