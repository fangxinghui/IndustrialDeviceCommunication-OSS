using System.Threading;
using System.Threading.Tasks;
using DeviceCommunication.Models;

namespace DeviceCommunication.Abstractions
{
    public interface ISerialPort : ICommPort
    {
        SerialPortConfiguration? Configuration { get; set; }

        /// <summary>
        /// 在已打开的串口上接收指定时长内的数据（不发送）。用于流式采集。
        /// </summary>
        Task<byte[]> ReceiveForDurationAsync(int durationMs, CancellationToken cancellationToken = default);
    }
}
