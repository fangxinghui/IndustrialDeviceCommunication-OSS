using System.ComponentModel;

using CoreShared;

namespace DeviceCommunication.Models
{
    public class ControllerTag:INotifyPropertyChanged
    {
        public ControllerAddress Address { get; set; } = new ControllerAddress();
        private object? _value;
        /// <summary>
        /// 绑定到UI，减少刷新
        /// </summary>
        public object? Value
        {
            get => _value;
            set
            {
                _value = value;
                OnPropertyChanged(nameof(Value));
            }
        }
        private object? _rtValue;
        /// <summary>
        /// 实时值
        /// </summary>
        public object? RtValue
        {
            get => _rtValue;
            set
            {
                _rtValue = value;
                OnPropertyChanged(nameof(RtValue));
            }
        }
        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}