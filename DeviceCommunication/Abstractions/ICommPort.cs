using System;
using System.Threading;
using System.Threading.Tasks;
using CoreShared.Events;

namespace DeviceCommunication.Abstractions
{
    public interface ICommPort
    {
        bool IsOpen { get; }
        bool Open();
        void Close();
        Task SendBytesAsync(byte[] data,CancellationToken cancellationToken = default);
        bool SendBytes(byte[] data);
        bool SendAsciiString(string data);
        /// <summary>
        /// 发送数据并等待返回响应
        /// </summary>
        /// <param name="data">要发送的字节数据</param>
        /// <param name="timeoutMs">超时时间（毫秒），默认 5000ms</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>接收到的响应字节数据</returns>
        Task<byte[]> SendAndReceiveAsync(byte[] data, int timeoutMs = 5000, CancellationToken cancellationToken = default);

        /// <summary>
        /// 发送 ASCII 字符串并等待返回响应
        /// </summary>
        /// <param name="data">要发送的 ASCII 字符串</param>
        /// <param name="timeoutMs">超时时间（毫秒），默认 5000ms</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>接收到的响应字符串</returns>
        Task<string> SendAndReceiveStringAsync(string data, int timeoutMs = 5000, CancellationToken cancellationToken = default);

        /// <summary>
        /// 发送 ASCII 字符串并持续等待，直到响应满足完成条件或超时
        /// </summary>
        /// <param name="data">要发送的 ASCII 字符串</param>
        /// <param name="isComplete">响应完成判断函数</param>
        /// <param name="timeoutMs">超时时间（毫秒）</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>接收到的完整响应字符串</returns>
        Task<string> SendAndReceiveStringAsync(
            string data,
            Func<string, bool> isComplete,
            int timeoutMs = 5000,
            CancellationToken cancellationToken = default);
        //初始化方法
        bool Initialize();
        Action<byte[], object>? OnDataReceived { get; set; }
        event EventHandler<ExceptionEvent>? OnException;
        event EventHandler<ExecuteLogEvent>? OnExecuteLog;
    }
}