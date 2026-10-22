using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DeviceCommunication.Abstractions;

namespace DeviceCommunication.Models
{
    public class NetConfiguration:IConfiguration,INotifyPropertyChanged
    {
        private string? _ipAddress = string.Empty;

        /// <summary>
        /// IP地址
        /// </summary>
        public string? IpAddress
        {
            get => _ipAddress;
            set=>SetField(ref _ipAddress, value);
        }

        private int _port = 502;

        /// <summary>
        /// 端口号
        /// </summary>
        public int Port
        {
            get => _port;
            set=>SetField(ref _port, value);
        }
        private string? _protocol = "VXI-11";

        /// <summary>
        /// 通讯协议，如 VXI-11
        /// </summary>
        public string? Protocol
        {
            get => _protocol;
            set=>SetField(ref _protocol, value);
        }

        public event PropertyChangedEventHandler PropertyChanged;

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