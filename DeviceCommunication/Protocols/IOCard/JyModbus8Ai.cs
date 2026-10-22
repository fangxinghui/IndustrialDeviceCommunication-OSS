using DeviceCommunication.Attributes;
using DeviceCommunication.Models;

namespace DeviceCommunication.Protocols.IOCard{
    /// <summary>
    /// 艾莫迅 JY-MODBUS-8AI：8 路 0~10V / 0~20mA 模拟量输入，Modbus RTU 功能码 0x04，寄存器 0x00~0x07。
    /// </summary>
    [ControllerDriver(brand: "AMSAMOTION", model: "JY-MODBUS-8AI", controllerType: ControllerType.AnalogModule)]
    public class JyModbus8Ai : ModbusAnalogInputBase
    {
        private const double FullScaleRaw = 16000;
        private const double FullScaleVoltage = 10.0;
        private const double FullScaleCurrentMa = 20.0;

        protected override int ChannelCount => 8;
        protected override int StartRegister => 0;
        protected override AnalogInputScaleMode DefaultScaleMode => AnalogInputScaleMode.Voltage0To10V;

        public JyModbus8Ai(ControllerConfiguration controllerConfiguration) : base(controllerConfiguration) { }

        protected override AnalogInputReading ConvertRawToSignal(ushort raw, AnalogInputChannelOptions options)
        {
            var inputRange = (options.InputRange ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(inputRange))
                inputRange = DefaultScaleMode == AnalogInputScaleMode.Current0To20mA ? "0-20mA" : "0-10V";

            if (inputRange.Equals("0-20mA", System.StringComparison.OrdinalIgnoreCase) ||
                inputRange.Equals("0~20mA", System.StringComparison.OrdinalIgnoreCase) ||
                inputRange.Equals("4-20mA", System.StringComparison.OrdinalIgnoreCase) ||
                inputRange.Equals("4~20mA", System.StringComparison.OrdinalIgnoreCase))
            {
                return new AnalogInputReading
                {
                    RawValue = raw,
                    SignalValue = raw * FullScaleCurrentMa / FullScaleRaw,
                    SignalUnit = "mA"
                };
            }

            if (inputRange.Equals("0-10V", System.StringComparison.OrdinalIgnoreCase) ||
                inputRange.Equals("0~10V", System.StringComparison.OrdinalIgnoreCase))
            {
                return new AnalogInputReading
                {
                    RawValue = raw,
                    SignalValue = raw * FullScaleVoltage / FullScaleRaw,
                    SignalUnit = "V"
                };
            }

            return new AnalogInputReading
            {
                RawValue = raw,
                SignalValue = raw
            };
        }
    }
}
