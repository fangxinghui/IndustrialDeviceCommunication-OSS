using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CoreShared.Events;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Models;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using IMqttClient = DeviceCommunication.Abstractions.IMqttClient;

namespace DeviceCommunication.Protocols.Mqtt
{
    /// <summary>
    /// 基于 MQTTnet 的 MQTT 客户端驱动。
    /// </summary>
    internal sealed class MqttClient : IMqttClient
    {
        private readonly MqttFactory _factory = new MqttFactory();
        private MQTTnet.Client.IMqttClient? _client;
        private MqttClientOptions? _lastOptions;
        private CancellationTokenSource? _reconnectCts;
        private int _reconnectAttempt;
        private bool _initialized;
        private bool _disposed;
        private bool _intentionalDisconnect;

        public MqttConfiguration? Configuration { get; set; }
        public string? Host { get; set; }
        public int Port { get; set; } = 1883;
        public string? ClientId { get; set; }
        public string? ServerName { get; set; }
        public bool AutoReconnect { get; set; } = true;
        public bool IsConnected => _client?.IsConnected ?? false;

        public event EventHandler? Connected;
        public event EventHandler? Disconnected;
        public event EventHandler<MqttMessageReceivedEventArgs>? MessageReceived;
        public event EventHandler<ExceptionEvent>? OnException;
        public event EventHandler<ExecuteLogEvent>? OnExecuteLog;

        public MqttClient(MqttConfiguration? configuration)
        {
            Configuration = configuration;
        }

        public bool Initialize()
        {
            if (_disposed)
                return false;

            try
            {
                ApplyConfiguration();

                if (_client != null)
                {
                    _initialized = true;
                    return true;
                }

                _client = _factory.CreateMqttClient();
                _client.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;
                _client.ConnectedAsync += OnConnectedAsync;
                _client.DisconnectedAsync += OnDisconnectedAsync;
                _initialized = true;

                RaiseLog(nameof(Initialize),
                    $"MQTT client initialized. Host={Host}, Port={Port}, ClientId={ClientId}");
                return true;
            }
            catch (Exception ex)
            {
                RaiseException(nameof(Initialize), ex);
                return false;
            }
        }

        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                return false;

            if (!_initialized && !Initialize())
                return false;

            if (_client is null)
                return false;

            try
            {
                _intentionalDisconnect = false;
                ApplyConfiguration();
                _lastOptions = BuildOptions();

                if (_client.IsConnected)
                    return true;

                RaiseLog(nameof(ConnectAsync), $"Connecting to MQTT broker {Host}:{Port} ...");
                await _client.ConnectAsync(_lastOptions, cancellationToken).ConfigureAwait(false);
                return _client.IsConnected;
            }
            catch (Exception ex)
            {
                RaiseException(nameof(ConnectAsync), ex);
                return false;
            }
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (_client is null)
                return;

            try
            {
                _intentionalDisconnect = true;
                StopReconnectLoop();
                if (_client.IsConnected)
                    await _client.DisconnectAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

                RaiseLog(nameof(DisconnectAsync), $"Disconnected from MQTT broker {Host}:{Port}");
            }
            catch (Exception ex)
            {
                RaiseException(nameof(DisconnectAsync), ex);
            }
        }

        public async Task SubscribeAsync(
            string topic,
            MqttQos qos = MqttQos.AtLeastOnce,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(topic))
                throw new ArgumentException("Topic is required.", nameof(topic));
            EnsureConnected();

            try
            {
                var filter = new MqttTopicFilterBuilder()
                    .WithTopic(topic)
                    .WithQualityOfServiceLevel(ToMqttQos(qos))
                    .Build();

                await _client!.SubscribeAsync(filter, cancellationToken).ConfigureAwait(false);
                RaiseLog(nameof(SubscribeAsync), $"Subscribed topic '{topic}' QoS={(int)qos}");
            }
            catch (Exception ex)
            {
                RaiseException(nameof(SubscribeAsync), ex);
                throw;
            }
        }

        public async Task UnsubscribeAsync(string topic, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(topic))
                throw new ArgumentException("Topic is required.", nameof(topic));
            EnsureConnected();

            try
            {
                await _client!.UnsubscribeAsync(topic, cancellationToken).ConfigureAwait(false);
                RaiseLog(nameof(UnsubscribeAsync), $"Unsubscribed topic '{topic}'");
            }
            catch (Exception ex)
            {
                RaiseException(nameof(UnsubscribeAsync), ex);
                throw;
            }
        }

        public Task PublishStringAsync(
            string topic,
            string payload,
            MqttQos qos = MqttQos.AtLeastOnce,
            bool retain = false,
            CancellationToken cancellationToken = default)
        {
            var bytes = string.IsNullOrEmpty(payload)
                ? Array.Empty<byte>()
                : Encoding.UTF8.GetBytes(payload);
            return PublishAsync(topic, bytes, qos, retain, cancellationToken);
        }

        public async Task PublishAsync(
            string topic,
            byte[] payload,
            MqttQos qos = MqttQos.AtLeastOnce,
            bool retain = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(topic))
                throw new ArgumentException("Topic is required.", nameof(topic));
            EnsureConnected();

            try
            {
                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(payload ?? Array.Empty<byte>())
                    .WithQualityOfServiceLevel(ToMqttQos(qos))
                    .WithRetainFlag(retain)
                    .Build();

                await _client!.PublishAsync(message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RaiseException(nameof(PublishAsync), ex);
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _intentionalDisconnect = true;
            StopReconnectLoop();

            try
            {
                if (_client != null)
                {
                    _client.ApplicationMessageReceivedAsync -= OnApplicationMessageReceivedAsync;
                    _client.ConnectedAsync -= OnConnectedAsync;
                    _client.DisconnectedAsync -= OnDisconnectedAsync;
                    if (_client.IsConnected)
                        _client.DisconnectAsync().GetAwaiter().GetResult();
                    _client.Dispose();
                }
            }
            catch (Exception ex)
            {
                RaiseException(nameof(Dispose), ex);
            }
            finally
            {
                _client = null;
            }
        }

        private void ApplyConfiguration()
        {
            var cfg = Configuration;
            if (cfg is null)
                return;

            if (!string.IsNullOrWhiteSpace(cfg.Host))
                Host = cfg.Host;
            if (cfg.Port > 0)
                Port = cfg.Port;
            if (!string.IsNullOrWhiteSpace(cfg.ClientId))
                ClientId = cfg.ClientId;
            if (!string.IsNullOrWhiteSpace(cfg.ServerName))
                ServerName = cfg.ServerName;

            AutoReconnect = cfg.AutoReconnect;
        }

        private MqttClientOptions BuildOptions()
        {
            if (string.IsNullOrWhiteSpace(Host))
                throw new InvalidOperationException("MQTT Host is required.");

            if (string.IsNullOrWhiteSpace(ClientId))
                ClientId = $"dc-{Guid.NewGuid():N}";

            var builder = new MqttClientOptionsBuilder()
                .WithTcpServer(Host, Port)
                .WithClientId(ClientId)
                .WithCleanSession();

            var user = Configuration?.UserName;
            var password = Configuration?.Password;
            if (!string.IsNullOrEmpty(user))
                builder.WithCredentials(user, password);

            return builder.Build();
        }

        private void EnsureConnected()
        {
            if (_client is null || !_client.IsConnected)
                throw new InvalidOperationException("MQTT client is not connected.");
        }

        private Task OnConnectedAsync(MqttClientConnectedEventArgs args)
        {
            _reconnectAttempt = 0;
            RaiseLog(nameof(OnConnectedAsync),
                $"Connected to MQTT broker {Host}:{Port}. Result={args.ConnectResult.ResultCode}");
            Connected?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
        {
            RaiseLog(nameof(OnDisconnectedAsync),
                $"Disconnected from MQTT broker {Host}:{Port}. Reason={args.Reason}");
            Disconnected?.Invoke(this, EventArgs.Empty);

            if (!_intentionalDisconnect && AutoReconnect && _lastOptions != null)
                StartReconnectLoop();

            return Task.CompletedTask;
        }

        private Task OnApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
        {
            try
            {
                var payload = args.ApplicationMessage.PayloadSegment.Count == 0
                    ? Array.Empty<byte>()
                    : args.ApplicationMessage.PayloadSegment.ToArray();

                MessageReceived?.Invoke(this, new MqttMessageReceivedEventArgs(
                    args.ApplicationMessage.Topic,
                    payload,
                    ClientId,
                    ServerName));
            }
            catch (Exception ex)
            {
                RaiseException(nameof(OnApplicationMessageReceivedAsync), ex);
            }

            return Task.CompletedTask;
        }

        private void StartReconnectLoop()
        {
            StopReconnectLoop();
            _reconnectCts = new CancellationTokenSource();
            var token = _reconnectCts.Token;
            var cfg = Configuration;
            var baseDelayMs = cfg != null && cfg.ReconnectBaseDelayMs > 0 ? cfg.ReconnectBaseDelayMs : 5000;
            var maxDelayMs = cfg != null && cfg.MaxReconnectDelayMs > 0 ? cfg.MaxReconnectDelayMs : 30000;
            var maxAttempts = cfg?.MaxReconnectAttempts ?? 0;

            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested &&
                       _client != null &&
                       !_client.IsConnected &&
                       (maxAttempts == 0 || _reconnectAttempt < maxAttempts))
                {
                    var delayMs = Math.Min(maxDelayMs, baseDelayMs * Math.Pow(2, _reconnectAttempt));
                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(delayMs), token).ConfigureAwait(false);
                        if (token.IsCancellationRequested || _lastOptions is null || _client is null)
                            break;

                        await _client.ConnectAsync(_lastOptions, token).ConfigureAwait(false);
                        if (_client.IsConnected)
                            break;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        RaiseException(nameof(StartReconnectLoop),
                            new Exception($"Reconnect attempt {_reconnectAttempt} failed: {ex.Message}", ex));
                    }

                    _reconnectAttempt++;
                }
            }, token);
        }

        private void StopReconnectLoop()
        {
            try
            {
                _reconnectCts?.Cancel();
                _reconnectCts?.Dispose();
            }
            catch
            {
                // ignore
            }
            finally
            {
                _reconnectCts = null;
            }
        }

        private static MqttQualityOfServiceLevel ToMqttQos(MqttQos qos) =>
            (MqttQualityOfServiceLevel)(int)qos;

        private void RaiseLog(string method, string message)
        {
            OnExecuteLog?.Invoke(this, new ExecuteLogEvent
            {
                ClassName = nameof(MqttClient),
                MethodName = method,
                LogMessage = message
            });
        }

        private void RaiseException(string method, Exception ex)
        {
            OnException?.Invoke(this, new ExceptionEvent
            {
                ClassName = nameof(MqttClient),
                MethodName = method,
                ErrorMessage = ex.Message
            });
        }
    }
}
