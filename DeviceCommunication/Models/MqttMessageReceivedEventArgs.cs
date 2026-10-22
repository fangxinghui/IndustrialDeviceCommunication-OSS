using System;

namespace DeviceCommunication.Models
{
    public sealed class MqttMessageReceivedEventArgs : EventArgs
    {
        public MqttMessageReceivedEventArgs(
            string topic,
            byte[] payload,
            string? clientId = null,
            string? serverName = null)
        {
            Topic = topic ?? string.Empty;
            Payload = payload ?? Array.Empty<byte>();
            ClientId = clientId;
            ServerName = serverName;
        }

        public string Topic { get; }
        public byte[] Payload { get; }
        public string? ClientId { get; }
        public string? ServerName { get; }

        public string GetPayloadString() =>
            Payload.Length == 0 ? string.Empty : System.Text.Encoding.UTF8.GetString(Payload);
    }
}
