using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DeviceCommunication.Abstractions;

namespace DeviceCommunication.Models
{
    /// <summary>
    /// MQTT 客户端 / Broker 共用配置。
    /// </summary>
    public class MqttConfiguration : IConfiguration, INotifyPropertyChanged
    {
        private string? _host = "127.0.0.1";
        private int _port = 1883;
        private string? _clientId;
        private string? _userName;
        private string? _password;
        private string? _serverName;
        private bool _autoReconnect = true;
        private int _reconnectBaseDelayMs = 5000;
        private int _maxReconnectAttempts;
        private int _maxReconnectDelayMs = 30000;
        private string? _boundAddress = "0.0.0.0";

        /// <summary>Broker 主机名或 IP（客户端用）。</summary>
        public string? Host
        {
            get => _host;
            set => SetField(ref _host, value);
        }

        /// <summary>端口，默认 1883。</summary>
        public int Port
        {
            get => _port;
            set => SetField(ref _port, value);
        }

        /// <summary>客户端 ID。</summary>
        public string? ClientId
        {
            get => _clientId;
            set => SetField(ref _clientId, value);
        }

        /// <summary>用户名（可选）。</summary>
        public string? UserName
        {
            get => _userName;
            set => SetField(ref _userName, value);
        }

        /// <summary>密码（可选）。</summary>
        public string? Password
        {
            get => _password;
            set => SetField(ref _password, value);
        }

        /// <summary>逻辑名称，便于多连接管理。</summary>
        public string? ServerName
        {
            get => _serverName;
            set => SetField(ref _serverName, value);
        }

        /// <summary>断线后是否自动重连（仅客户端）。</summary>
        public bool AutoReconnect
        {
            get => _autoReconnect;
            set => SetField(ref _autoReconnect, value);
        }

        /// <summary>重连初始间隔（毫秒）。</summary>
        public int ReconnectBaseDelayMs
        {
            get => _reconnectBaseDelayMs;
            set => SetField(ref _reconnectBaseDelayMs, value);
        }

        /// <summary>最大重连次数；0 表示无限。</summary>
        public int MaxReconnectAttempts
        {
            get => _maxReconnectAttempts;
            set => SetField(ref _maxReconnectAttempts, value);
        }

        /// <summary>重连退避上限（毫秒）。</summary>
        public int MaxReconnectDelayMs
        {
            get => _maxReconnectDelayMs;
            set => SetField(ref _maxReconnectDelayMs, value);
        }

        /// <summary>Broker 监听地址（服务端用），默认 0.0.0.0。</summary>
        public string? BoundAddress
        {
            get => _boundAddress;
            set => SetField(ref _boundAddress, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
