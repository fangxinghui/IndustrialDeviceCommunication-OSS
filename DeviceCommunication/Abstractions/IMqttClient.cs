using System;
using System.Threading;
using System.Threading.Tasks;
using CoreShared.Events;
using DeviceCommunication.Models;

namespace DeviceCommunication.Abstractions
{
    /// <summary>
    /// MQTT 客户端驱动抽象（连接 Broker、订阅/发布）。
    /// </summary>
    public interface IMqttClient : IDisposable
    {
        MqttConfiguration? Configuration { get; set; }
        string? Host { get; set; }
        int Port { get; set; }
        string? ClientId { get; set; }
        string? ServerName { get; set; }
        bool IsConnected { get; }
        bool AutoReconnect { get; set; }

        bool Initialize();
        Task<bool> ConnectAsync(CancellationToken cancellationToken = default);
        Task DisconnectAsync(CancellationToken cancellationToken = default);
        Task SubscribeAsync(string topic, MqttQos qos = MqttQos.AtLeastOnce, CancellationToken cancellationToken = default);
        Task UnsubscribeAsync(string topic, CancellationToken cancellationToken = default);
        Task PublishAsync(
            string topic,
            byte[] payload,
            MqttQos qos = MqttQos.AtLeastOnce,
            bool retain = false,
            CancellationToken cancellationToken = default);
        Task PublishStringAsync(
            string topic,
            string payload,
            MqttQos qos = MqttQos.AtLeastOnce,
            bool retain = false,
            CancellationToken cancellationToken = default);

        event EventHandler? Connected;
        event EventHandler? Disconnected;
        event EventHandler<MqttMessageReceivedEventArgs>? MessageReceived;
        event EventHandler<ExceptionEvent>? OnException;
        event EventHandler<ExecuteLogEvent>? OnExecuteLog;
    }
}
