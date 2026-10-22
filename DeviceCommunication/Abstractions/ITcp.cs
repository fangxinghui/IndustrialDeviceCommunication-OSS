using System;
using System.Threading;
using System.Threading.Tasks;
using CoreShared.Events;
using DeviceCommunication.Models;

namespace DeviceCommunication.Abstractions
{
    public interface ITcp:IDisposable,ICommPort
    {   
        NetConfiguration? Configuration { get; set; }
        string? IpAddress { get; set; }
        int Port { get; set; }
        Task<byte[]> ReceiveAsync(int byteCount, CancellationToken cancellationToken = default);
        Task<byte[]> ReceiveAsync(CancellationToken cancellationToken = default);
        byte[] Receive(int byteCount);
    }
}