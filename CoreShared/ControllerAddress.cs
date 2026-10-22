using System;
using System.ComponentModel;

namespace CoreShared
{

    #region 控制器地址方向

    public enum ControllerAddressDirection
    {
        Input,
        Output
    }

    #endregion

    #region 控制器地址类型

    public enum ControllerAddressType
    {
        DigitalInput,
        DigitalOutput,
        AnalogInput,
        AnalogOutput,
        RegisterData
    }

    #endregion

    /// <summary>
    /// 统一控制器地址模型 — UI 层（方向/通道/点位）+ 通讯层（寄存器地址/数据类型）共用
    /// </summary>
    public class ControllerAddress : INotifyPropertyChanged
    {
        private int _id;

        public int Id
        {
            get => _id;
            set
            {
                if (_id != value)
                {
                    _id = value;
                    OnPropertyChanged();
                }
            }
        }

        private string? _name;

        public string? Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        private string? _displayName;

        public string? DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    OnPropertyChanged();
                }
            }
        }

        // ===== 控制器归属 =====

        private string _controllerId = string.Empty;

        /// <summary>所属控制器的 Id</summary>
        public string ControllerId
        {
            get => _controllerId;
            set
            {
                if (_controllerId != value)
                {
                    _controllerId = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _controllerName = string.Empty;

        /// <summary>所属控制器名称</summary>
        public string ControllerName
        {
            get => _controllerName;
            set
            {
                if (_controllerName != value)
                {
                    _controllerName = value;
                    OnPropertyChanged();
                }
            }
        }

        // ===== 信号方向与类型（UI 层） =====

        private ControllerAddressDirection _direction = ControllerAddressDirection.Output;

        /// <summary>信号方向：Input / Output</summary>
        public ControllerAddressDirection Direction
        {
            get => _direction;
            set
            {
                if (_direction != value)
                {
                    _direction = value;
                    OnPropertyChanged();
                }
            }
        }

        private ControllerAddressType _addressType = ControllerAddressType.DigitalInput;

        /// <summary>信号类型：DigitalInput / DigitalOutput / AnalogInput / AnalogOutput / RegisterData</summary>
        public ControllerAddressType AddressType
        {
            get => _addressType;
            set
            {
                if (_addressType != value)
                {
                    _addressType = value;
                    OnPropertyChanged();
                }
            }
        }

        private string? _groupId;

        /// <summary>分组 Id</summary>
        public string? GroupId
        {
            get => _groupId;
            set
            {
                if (_groupId != value)
                {
                    _groupId = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _channel = string.Empty;

        /// <summary>通道号</summary>
        public string Channel
        {
            get => _channel;
            set
            {
                if (_channel != value)
                {
                    _channel = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _point = string.Empty;

        /// <summary>通道内点位</summary>
        public string Point
        {
            get => _point;
            set
            {
                if (_point != value)
                {
                    _point = value;
                    OnPropertyChanged();
                }
            }
        }

        // ===== 寄存器地址（通讯层） =====

        private string? _address;

        /// <summary>实际寄存器地址字符串（如 "0"、"0x0001"、"DB1.0"）</summary>
        public string? Address
        {
            get => _address;
            set
            {
                if (_address != value)
                {
                    _address = value;
                    OnPropertyChanged();
                }
            }
        }

        private DataConvert.DataType _dataType = DataConvert.DataType.Bool;

        /// <summary>数据类型（决定如何解析原始字节）</summary>
        public DataConvert.DataType DataType
        {
            get => _dataType;
            set
            {
                if (_dataType != value)
                {
                    _dataType = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _length = 1;

        /// <summary>数据长度</summary>
        public int Length
        {
            get => _length;
            set
            {
                if (_length != value)
                {
                    _length = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _reverse;

        /// <summary>
        /// DigitalInput 读取值是否取反（true：硬件 false 对外为 true，反之亦然）。
        /// </summary>
        public bool Reverse
        {
            get => _reverse;
            set
            {
                if (_reverse != value)
                {
                    _reverse = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// 对读取结果做地址级变换：DigitalInput 且 <see cref="Reverse"/> 时取反布尔值。
        /// </summary>
        public object? TransformReadValue(object? value)
        {
            if (value is null || !Reverse || AddressType != ControllerAddressType.DigitalInput)
                return value;

            try
            {
                return !Convert.ToBoolean(value);
            }
            catch
            {
                return value;
            }
        }

        // ===== 显示与状态 =====

        private bool _isDisplay;

        /// <summary>是否在 UI 中显示</summary>
        public bool IsDisplay
        {
            get => _isDisplay;
            set
            {
                if (_isDisplay != value)
                {
                    _isDisplay = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isEnabled = true;

        /// <summary>是否启用</summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        // ===== 运行时值 =====

        private object? _value;

        /// <summary>当前值（轮询更新）</summary>
        [System.Xml.Serialization.XmlIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public object? Value
        {
            get => _value;
            set
            {
                if (!Equals(_value, value))
                {
                    _value = value;
                    OnPropertyChanged();
                }
            }
        }

        // ===== ToString =====

        public override string ToString()
            => !string.IsNullOrWhiteSpace(Address)
                ? $"{ControllerName}/{AddressType}/{Direction}/{Channel}/{Point} ({Address})"
                : $"{ControllerName}/{AddressType}/{Direction}/{Channel}/{Point}";

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        #endregion
    }
}