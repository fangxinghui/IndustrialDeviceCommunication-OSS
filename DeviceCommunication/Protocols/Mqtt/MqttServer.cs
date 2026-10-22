using System;
using System.Threading;
using System.Threading.Tasks;
using CoreShared.Events;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Models;
using MQTTnet;
using MQTTnet.Server;

namespace DeviceCommunication.Protocols.Mqtt
{
    /// <summary>
    /// 基于 MQTTnet 的嵌入式 MQTT Broker 驱动。
    /// </summary>
    internal sealed class MqttServer : IMqttServer
    {
        private readonly MqttFactory _factory = new MqttFactory();
        private MQTTnet.Server.MqttServer? _server;
        private bool _initialized;
        private bool _disposed;
        private bool _started;

        public MqttConfiguration? Configuration { get; set; }
        public string? BoundAddress { get; set; } = "0.0.0.0";
        public int Port { get; set; } = 1883;
        public bool IsStarted => _started && _server != null;

        public event EventHandler<MqttClientConnectionEventArgs>? ClientConnected;
        public event EventHandler<MqttClientConnectionEventArgs>? ClientDisconnected;
        public event EventHandler<MqttMessageReceivedEventArgs>? MessageReceived;
        public event EventHandler<ExceptionEvent>? OnException;
        public event EventHandler<ExecuteLogEvent>? OnExecuteLog;

        public MqttServer(MqttConfiguration? configuration)
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

                if (_server != null)
                {
                    _initialized = true;
                    return true;
                }

                var options = new MqttServerOptionsBuilder()
                    .WithDefaultEndpoint()
                    .WithDefaultEndpointPort(Port)
                    .Build();

                // BoundAddress: MQTTnet 4.x 默认监听所有接口；显式绑定需额外 endpoint，此处先记录配置。
                _server = _factory.CreateMqttServer(options);
                _server.ClientConnectedAsync += OnClientConnectedAsync;
                _server.ClientDisconnectedAsync += OnClientDisconnectedAsync;
                _server.InterceptingPublishAsync += OnInterceptingPublishAsync;

                _initialized = true;
                RaiseLog(nameof(Initialize),
                    $"MQTT server initialized. BoundAddress={BoundAddress}, Port={Port}");
                return true;
            }
            catch (Exception ex)
            {
                RaiseException(nameof(Initialize), ex);
                return false;
            }
        }

        public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                return false;

            if (!_initialized && !Initialize())
                return false;

            if (_server is null)
                return false;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_started)
                    return true;

                await _server.StartAsync().ConfigureAwait(false);
                _started = true;
                RaiseLog(nameof(StartAsync), $"MQTT broker started on port {Port}");
                return true;
            }
            catch (Exception ex)
            {
                RaiseException(nameof(StartAsync), ex);
                return false;
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (_server is null || !_started)
                return;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _server.StopAsync().ConfigureAwait(false);
                _started = false;
                RaiseLog(nameof(StopAsync), $"MQTT broker stopped on port {Port}");
            }
            catch (Exception ex)
            {
                RaiseException(nameof(StopAsync), ex);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                if (_server != null)
                {
                    _server.ClientConnectedAsync -= OnClientConnectedAsync;
                    _server.ClientDisconnectedAsync -= OnClientDisconnectedAsync;
                    _server.InterceptingPublishAsync -= OnInterceptingPublishAsync;

                    if (_started)
                        _server.StopAsync().GetAwaiter().GetResult();

                    _server.Dispose();
                }
            }
            catch (Exception ex)
            {
                RaiseException(nameof(Dispose), ex);
            }
            finally
            {
                _started = false;
                _server = null;
            }
        }

        private void ApplyConfiguration()
        {
            var cfg = Configuration;
            if (cfg is null)
                return;

            if (cfg.Port > 0)
                Port = cfg.Port;
            if (!string.IsNullOrWhiteSpace(cfg.BoundAddress))
                BoundAddress = cfg.BoundAddress;
        }

        private Task OnClientConnectedAsync(ClientConnectedEventArgs args)
        {
            RaiseLog(nameof(OnClientConnectedAsync), $"Client connected: {args.ClientId}");
            ClientConnected?.Invoke(this, new MqttClientConnectionEventArgs(args.ClientId));
            return Task.CompletedTask;
        }

        private Task OnClientDisconnectedAsync(ClientDisconnectedEventArgs args)
        {
            RaiseLog(nameof(OnClientDisconnectedAsync), $"Client disconnected: {args.ClientId}");
            ClientDisconnected?.Invoke(this, new MqttClientConnectionEventArgs(args.ClientId));
            return Task.CompletedTask;
        }

        private Task OnInterceptingPublishAsync(InterceptingPublishEventArgs args)
        {
            try
            {
                var payload = args.ApplicationMessage.PayloadSegment.Count == 0
                    ? Array.Empty<byte>()
                    : args.ApplicationMessage.PayloadSegment.ToArray();

                MessageReceived?.Invoke(this, new MqttMessageReceivedEventArgs(
                    args.ApplicationMessage.Topic,
                    payload,
                    args.ClientId,
                    Configuration?.ServerName));
            }
            catch (Exception ex)
            {
                RaiseException(nameof(OnInterceptingPublishAsync), ex);
            }

            return Task.CompletedTask;
        }

        private void RaiseLog(string method, string message)
        {
            OnExecuteLog?.Invoke(this, new ExecuteLogEvent
            {
                ClassName = nameof(MqttServer),
                MethodName = method,
                LogMessage = message
            });
        }

        private void RaiseException(string method, Exception ex)
        {
            OnException?.Invoke(this, new ExceptionEvent
            {
                ClassName = nameof(MqttServer),
                MethodName = method,
                ErrorMessage = ex.Message
            });
        }
    }
}
