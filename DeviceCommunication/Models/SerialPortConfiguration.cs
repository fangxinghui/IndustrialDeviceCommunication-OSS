using DeviceCommunication.Abstractions;
using System.ComponentModel;
namespace DeviceCommunication.Models
{
    public class SerialPortConfiguration:IConfiguration,INotifyPropertyChanged
    {
        private string? _portName;
        /// <summary>
        /// 端口名称
        /// </summary>
        public string? PortName
        {
            get=> _portName;
            set
            {
                if (_portName == value) return;
                _portName = value;
                OnPropertyChanged(nameof(PortName));
            }
        }
        private int _baudRate = 9600;
        /// <summary>
        /// 波特率
        /// </summary>
        public int BaudRate
        {
            get => _baudRate;
            set
            {
                if (_baudRate == value) return;
                _baudRate = value;
                OnPropertyChanged(nameof(BaudRate));
            }
        }
        private int _dataBits = 8;
        /// <summary>
        /// 数据位（7 或 8）
        /// </summary>
        public int DataBits
        {
            get => _dataBits;
            set
            {
                if (_dataBits == value) return;
                _dataBits = value;
                OnPropertyChanged(nameof(DataBits));
            }
        }
        private SerialParameters.SerialParity _serialParity= SerialParameters.SerialParity.None;
        /// <summary>
        /// 校验位
        /// </summary>
        public SerialParameters.SerialParity Parity
        {
            get => _serialParity;
            set
            {
                if (_serialParity == value) return;
                _serialParity = value;
                OnPropertyChanged(nameof(Parity));
            }
        }
        private SerialParameters.SerialStopBits _stopBits = SerialParameters.SerialStopBits.One;
        /// <summary>
        /// 停止位
        /// </summary>

        public SerialParameters.SerialStopBits StopBits
        {
            get=> _stopBits;
            set
            {
                if (_stopBits == value) return;
                _stopBits = value;
                OnPropertyChanged(nameof(StopBits));
            }
        }
        private SerialParameters.SerialHandshake _handshake = SerialParameters.SerialHandshake.None;

        public SerialParameters.SerialHandshake Handshake
        {
            get=>_handshake;
            set
            {
                if (_handshake == value) return;
                _handshake = value;
                OnPropertyChanged(nameof(Handshake));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
