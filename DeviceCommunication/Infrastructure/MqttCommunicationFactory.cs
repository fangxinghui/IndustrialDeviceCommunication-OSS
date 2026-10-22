using System;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Models;

namespace DeviceCommunication.Infrastructure
{
    /// <summary>
    /// MQTT 客户端 / Broker 工厂（对齐 <see cref="TcpFactory"/>）。
    /// </summary>
    public static class MqttCommunicationFactory
    {
        public static IMqttClient CreateClient(MqttConfiguration configuration)
        {
            if (configuration is null)
                throw new ArgumentNullException(nameof(configuration));

            var client = new Protocols.Mqtt.MqttClient(configuration);
            if (!client.Initialize())
                throw new InvalidOperationException("Failed to initialize MQTT client.");
            return client;
        }

        public static IMqttServer CreateServer(MqttConfiguration configuration)
        {
            if (configuration is null)
                throw new ArgumentNullException(nameof(configuration));

            var server = new Protocols.Mqtt.MqttServer(configuration);
            if (!server.Initialize())
                throw new InvalidOperationException("Failed to initialize MQTT server.");
            return server;
        }

        public static object Create(MqttConfiguration configuration, MqttType type)
        {
            return type == MqttType.Client
                ? (object)CreateClient(configuration)
                : CreateServer(configuration);
        }
    }
}
