using System;
using System.ComponentModel;
using Newtonsoft.Json;

namespace DeviceCommunication.Models
{
    [Serializable]
    public class Sensor:INotifyPropertyChanged
    {
        private string? _name=string.Empty;
        /// <summary>
        /// sensor名称
        /// </summary>
        public string Name
        {
            get => _name ?? string.Empty;
            set
            {
                if(string.IsNullOrEmpty(value))return;
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        private bool _enable = true;
        /// <summary>
        /// sensor 可不可用
        /// </summary>
        public bool Enable
        {
            get => _enable;
            set
            {
                _enable = value;
                OnPropertyChanged(nameof(Enable));
            }
        }
        private ControllerTag? _controllerTag;
        /// <summary>
        /// 控制器地址
        /// </summary>
        [JsonIgnore]
        public ControllerTag? ControllerTag
        {
            get => _controllerTag;
            set
            {
                if(ReferenceEquals(_controllerTag, value)) return;
                if (_controllerTag !=null)
                {
                    _controllerTag.PropertyChanged -= ControllerTag_PropertyChanged;
                }

                _controllerTag = value;
                if(_controllerTag !=null)
                    _controllerTag.PropertyChanged += ControllerTag_PropertyChanged;
                OnPropertyChanged(nameof(ControllerTag));
                OnPropertyChanged(nameof(ControllerValue));
                OnPropertyChanged(nameof(ControllerBoolValue));
            }
        }
        /// <summary>
        /// 控制器值
        /// </summary>
        [JsonIgnore] public object? ControllerValue => _controllerTag?.Value;
        /// <summary>
        /// 控制器值 布尔类型
        /// </summary>
        [JsonIgnore] public bool? ControllerBoolValue => _controllerTag?.Value as bool?;
        private void ControllerTag_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ControllerTag.Value))
            {
                OnPropertyChanged(nameof(ControllerTag));
            }
        }
        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}