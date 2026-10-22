using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

namespace DeviceCommunication.Models
{
    [XmlRoot("Controllers")]
    public class ControllerCatalog : INotifyPropertyChanged
    {
        private ObservableCollection<ControllerCategoryItem> _controllers = new();

        [XmlElement("Controller")]
        public ObservableCollection<ControllerCategoryItem> Controllers
        {
            get => _controllers;
            set
            {
                if (_controllers != value)
                {
                    _controllers = value ?? new ObservableCollection<ControllerCategoryItem>();
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    [XmlRoot("Controller")]
    public class ControllerCategoryItem : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        [XmlElement("Id")]
        public string Id
        {
            get => _id;
            set { if (_id != value) { _id = value; OnPropertyChanged(); } }
        }

        private string _controllerNameEN = string.Empty;
        [XmlElement("ControllerNameEN")]
        public string ControllerNameEN
        {
            get => _controllerNameEN;
            set { if (_controllerNameEN != value) { _controllerNameEN = value; OnPropertyChanged(); } }
        }

        private string _controllerNameCN = string.Empty;
        [XmlElement("ControllerNameCN")]
        public string ControllerNameCN
        {
            get => _controllerNameCN;
            set { if (_controllerNameCN != value) { _controllerNameCN = value; OnPropertyChanged(); } }
        }

        private ControllerKind _controllerKind = ControllerKind.DigitalIo;
        [XmlElement("ControllerKind")]
        public ControllerKind ControllerKind
        {
            get => _controllerKind;
            set { if (_controllerKind != value) { _controllerKind = value; OnPropertyChanged(); } }
        }

        private string _icon = string.Empty;
        [XmlElement("ControllerIcon")]
        public string ControllerIcon
        {
            get => _icon;
            set { if (_icon != value) { _icon = value; OnPropertyChanged(); } }
        }

        private string _accentColor = "#22d3ee";
        [XmlElement("AccentColor")]
        public string AccentColor
        {
            get => _accentColor;
            set { if (_accentColor != value) { _accentColor = value; OnPropertyChanged(); } }
        }

        private string _backgroundColor = "#164e63";
        [XmlElement("BackgroundColor")]
        public string BackgroundColor
        {
            get => _backgroundColor;
            set { if (_backgroundColor != value) { _backgroundColor = value; OnPropertyChanged(); } }
        }

        private string _borderColor = "#0e7490";
        [XmlElement("BorderColor")]
        public string BorderColor
        {
            get => _borderColor;
            set { if (_borderColor != value) { _borderColor = value; OnPropertyChanged(); } }
        }

        private bool _isSelected;
        [XmlIgnore]
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        private ObservableCollection<ControllerBrandItem> _brands = new();
        [XmlArray("Brands")]
        [XmlArrayItem("Brand")]
        public ObservableCollection<ControllerBrandItem> Brands
        {
            get => _brands;
            set
            {
                if (_brands != value)
                {
                    _brands = value ?? new ObservableCollection<ControllerBrandItem>();
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class ControllerBrandItem : INotifyPropertyChanged
    {
        private string _brandCN = string.Empty;
        [XmlElement("BrandCN")]
        public string BrandCN
        {
            get => _brandCN;
            set { if (_brandCN != value) { _brandCN = value; OnPropertyChanged(); } }
        }

        private string _brandEN = string.Empty;
        [XmlElement("BrandEN")]
        public string BrandEN
        {
            get => _brandEN;
            set { if (_brandEN != value) { _brandEN = value; OnPropertyChanged(); } }
        }

        private ObservableCollection<ControllerModelItem> _models = new();
        [XmlArray("Models")]
        [XmlArrayItem("Model")]
        public ObservableCollection<ControllerModelItem> Models
        {
            get => _models;
            set
            {
                if (_models != value)
                {
                    _models = value ?? new ObservableCollection<ControllerModelItem>();
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class ControllerModelItem : INotifyPropertyChanged
    {
        private string _modelName = string.Empty;
        [XmlElement("ModelName")]
        public string ModelName
        {
            get => _modelName;
            set { if (_modelName != value) { _modelName = value; OnPropertyChanged(); } }
        }

        private ObservableCollection<string> _connectionTypes = new();
        [XmlArray("ConnectionTypes")]
        [XmlArrayItem("ConnectionType")]
        public ObservableCollection<string> ConnectionTypes
        {
            get => _connectionTypes;
            set
            {
                if (_connectionTypes != value)
                {
                    _connectionTypes = value ?? new ObservableCollection<string>();
                    OnPropertyChanged();
                }
            }
        }

        [XmlIgnore]
        public string ConnectionType
        {
            get => ConnectionTypes.FirstOrDefault() ?? string.Empty;
            set
            {
                ConnectionTypes.Clear();
                if (!string.IsNullOrWhiteSpace(value)) ConnectionTypes.Add(value);
                OnPropertyChanged(nameof(ConnectionType));
                OnPropertyChanged(nameof(ConnectionTypes));
            }
        }

        private int _channelCount = 8;
        [XmlElement("ChannelCount")]
        public int ChannelCount
        {
            get => _channelCount;
            set { if (_channelCount != value) { _channelCount = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}