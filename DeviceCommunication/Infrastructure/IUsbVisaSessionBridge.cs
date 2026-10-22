using System;

namespace DeviceCommunication.Infrastructure
{
    internal interface IUsbVisaSessionBridge : IDisposable
    {
        bool IsOpen { get; }
        bool TryOpen(string resourceName, int timeoutMs, out string? errorMessage);
        void WriteBytes(byte[] data);
        void WriteString(string data);
        byte[] ReadBytes();
        string ReadString();
        /// <summary>发送并读取。成功返回 true；掉线/会话关闭返回 false（不抛断开异常）。</summary>
        bool TryWriteAndReadString(string data, out string response);
        /// <summary>发送并读取；会话不可用时抛 IOException。</summary>
        string WriteAndReadString(string data);
        void ConfigureReadTermination(byte terminationChar, bool enabled);
        void TryClear();
        void SetTimeout(int timeoutMs);
    }
}
