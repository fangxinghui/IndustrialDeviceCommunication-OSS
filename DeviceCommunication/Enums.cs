namespace DeviceCommunication
{
    public sealed class SerialParameters
    {
        public enum SerialParity
        {
            None = 0,
            Odd = 1,
            Even = 2,
            Mark = 3,
            Space = 4
        }
        public enum SerialStopBits
        {
            One = 0,
            OnePointFive = 1,
            Two = 2,
        }
        public enum SerialHandshake
        {
            None = 0,
            XOnXOff = 1,
            RequestToSend = 2,
            RequestToSendXOnXOff = 3
        }
    }
    public enum TcpType
    {
        Client,
        Server
    }

    public enum MqttType
    {
        Client,
        Server
    }

    /// <summary>
    /// MQTT QoS（与 MQTT 协议 0/1/2 对齐）。
    /// </summary>
    public enum MqttQos
    {
        AtMostOnce = 0,
        AtLeastOnce = 1,
        ExactlyOnce = 2
    }
    public enum ConnectionType
    {
        SerialPort,
        Network,
    }
    public enum Brand
    {
        BeckHoff,
        Siemens,
        Mitsubishi,
        Delta,
        Omron,
        SchneiderElectric,
        ABB,
        RockwellAutomation,
        GeneralElectric,
        FujiElectric,
        Modbus,
        Panasonic,
        Keyence,
        Mock
    }
    public enum ControllerType
    {
        PLC,
        MotionalCard,
        AnalogModule,
        DigitalIoModule,
        DataAcquisition
    }

    /// <summary>
    /// Controller.xml 中的控制器分类（与 UI / 配置 Kind 对齐）。
    /// </summary>
    public enum ControllerKind
    {
        Plc,
        Motion,
        Analog,
        DigitalIo,
        Daq,
    }
    public enum ModbusDataType
    {
        ABCD,
        BADC,
        CDAB,
        DCBA
    }

    /// <summary>
    /// Modbus 模拟量输入工程值换算模式。
    /// </summary>
    public enum AnalogInputScaleMode
    {
        Raw,
        Voltage0To10V,
        Current0To20mA,
        Current4To20mA
    }

    public enum Availability
    {
        Disable,
        Enable
    }
}