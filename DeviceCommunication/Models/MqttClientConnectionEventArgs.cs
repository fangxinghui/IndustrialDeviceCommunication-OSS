using System;

namespace DeviceCommunication.Models
{
    public sealed class MqttClientConnectionEventArgs : EventArgs
    {
        public MqttClientConnectionEventArgs(string clientId)
        {
            ClientId = clientId ?? string.Empty;
        }

        public string ClientId { get; }
    }
}
