using System;
using System.Threading;
using System.Threading.Tasks;
using CoreShared.Events;
using DeviceCommunication.Models;

namespace DeviceCommunication.Abstractions
{
    /// <summary>
    /// 嵌入式 MQTT Broker 驱动抽象。
    /// </summary>
    public interface IMqttServer : IDisposable
    {
        MqttConfiguration? Configuration { get; set; }
        string? BoundAddress { get; set; }
        int Port { get; set; }
        bool IsStarted { get; }

        bool Initialize();
        Task<bool> StartAsync(CancellationToken cancellationToken = default);
        Task StopAsync(CancellationToken cancellationToken = default);

        event EventHandler<MqttClientConnectionEventArgs>? ClientConnected;
        event EventHandler<MqttClientConnectionEventArgs>? ClientDisconnected;
        event EventHandler<MqttMessageReceivedEventArgs>? MessageReceived;
        event EventHandler<ExceptionEvent>? OnException;
        event EventHandler<ExecuteLogEvent>? OnExecuteLog;
    }
}
