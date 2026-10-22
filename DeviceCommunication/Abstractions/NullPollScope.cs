using System;

namespace DeviceCommunication.Abstractions
{
    /// <summary>非共享总线控制器使用的空轮询会话。</summary>
    public sealed class NullPollScope : IDisposable
    {
        public static readonly NullPollScope Instance = new();

        private NullPollScope() { }

        public void Dispose() { }
    }
}
