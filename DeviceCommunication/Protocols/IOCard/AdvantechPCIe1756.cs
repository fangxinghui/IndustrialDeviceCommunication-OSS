using CoreShared;
using CoreShared.Events;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Attributes;
using DeviceCommunication.Models;
using System;
using System.Linq;

namespace DeviceCommunication.Protocols.IOCard
{
    /// <summary>
    /// 研华 PCIE-1756 / PCI-1756 PCIe 数字 I/O 卡控制器驱动。
    /// 32 路隔离 DI + 32 路隔离 DO（各 4 个 8 位端口，共 64 通道）。
    /// 通过 Automation.BDaq 托管 API 驱动。
    ///
    /// 设备名称通过 ControllerConfiguration.Parameters 传入，
    /// 格式如 "PCIE-1756,BID#0"。
    /// 地址表 Address 填逻辑通道 0–31（DI/DO 各自独立编号）。
    /// </summary>
    [ControllerDriver(brand: "Advantech", model: "PCIE-1756", controllerType: ControllerType.DigitalIoModule)]
    [ControllerDriver(brand: "Advantech", model: "PCI-1756", controllerType: ControllerType.DigitalIoModule)]
    internal class AdvantechPCIe1756 : IDigitalIo, IController
    {
        private const int MaxChannel = 31;
        private readonly ControllerConfiguration _configuration;
        private BDaqInterop? _daq;
        private bool _disposed;

        public event EventHandler<ExceptionEvent>? OnException;
        public event EventHandler<ExecuteLogEvent>? OnExecuteLog;

        public bool IsConnected => _daq?.Initialized ?? false;

        public AdvantechPCIe1756(ControllerConfiguration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        // ========== IController ==========

        public IConfiguration? CreateConfiguration() => null;

        public bool Initialize<T>(T parameter) => Connect(0);

        public bool Connect(int timeout)
        {
            try
            {
                Disconnect();

                var deviceName = NormalizeBDaqDeviceName(_configuration.Parameters);
                _daq = new BDaqInterop();
                return _daq.Load(deviceName);
            }
            catch (Exception ex)
            {
                OnException?.Invoke(this, new ExceptionEvent
                { ClassName = nameof(AdvantechPCIe1756), MethodName = nameof(Connect), ErrorMessage = ex.Message });
                return false;
            }
        }

        public void Disconnect()
        {
            _daq?.Dispose();
            _daq = null;
        }

        /// <summary>
        /// BDaq DeviceInformation 只要 "PCIE-1756,BID#0"，
        /// 兼容 UI 写入的 "PCIE://PCIE-1756,BID#0"。
        /// </summary>
        private static string NormalizeBDaqDeviceName(string? parameters)
        {
            var text = (parameters ?? string.Empty).Trim();
            foreach (var prefix in new[] { "PCIE://", "PCI://", "BDaq://" })
            {
                if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                text = text[prefix.Length..].Trim();
                break;
            }

            var semi = text.IndexOf(';');
            if (semi >= 0)
                text = text[..semi].Trim();

            return string.IsNullOrWhiteSpace(text) ? "PCIE-1756,BID#0" : text;
        }

        /// <summary>从 ControllerAddress 中解析通道索引 (0-31)</summary>
        private static bool TryGetChannel(ControllerAddress? address, out int channel)
        {
            channel = -1;
            if (address == null) return false;

            if (!string.IsNullOrWhiteSpace(address.Address) && int.TryParse(address.Address, out var parsed))
            {
                channel = parsed;
                return channel >= 0 && channel <= MaxChannel;
            }

            if (!string.IsNullOrWhiteSpace(address.Channel) && int.TryParse(address.Channel, out parsed))
            {
                channel = parsed >= 1 ? parsed - 1 : parsed;
                return channel >= 0 && channel <= MaxChannel;
            }

            if (!string.IsNullOrWhiteSpace(address.Point) && int.TryParse(address.Point, out parsed))
            {
                channel = parsed >= 1 ? parsed - 1 : parsed;
                return channel >= 0 && channel <= MaxChannel;
            }

            return false;
        }

        /// <summary>拆分通道号到 port (0-3) 和 bit (0-7)</summary>
        private static void GetPortAndBit(int channel, out int port, out int bit)
        {
            port = channel / 8;
            bit = channel % 8;
        }

        public (bool success, object value) ReadValue(ControllerAddress? address)
        {
            if (!IsConnected || address is null || !TryGetChannel(address, out var ch))
                return (false, null!);

            try
            {
                GetPortAndBit(ch, out var port, out var bit);
                // DI/DO 各自独立：输出轮询必须走 InstantDo.Read，不能读 InstantDi
                var portVal = IsDigitalOutput(address)
                    ? _daq!.ReadOutputPort(port)
                    : _daq!.ReadPort(port);
                return (true, (portVal & (1 << bit)) != 0);
            }
            catch (Exception ex)
            {
                OnException?.Invoke(this, new ExceptionEvent
                { ClassName = nameof(AdvantechPCIe1756), MethodName = nameof(ReadValue), ErrorMessage = ex.Message });
                return (false, null!);
            }
        }

        /// <summary>
        /// 优先用 AddressType；未标明数字 IO 类型时回退到 Direction。
        /// </summary>
        private static bool IsDigitalOutput(ControllerAddress address)
        {
            return address.AddressType switch
            {
                ControllerAddressType.DigitalOutput => true,
                ControllerAddressType.DigitalInput => false,
                _ => address.Direction == ControllerAddressDirection.Output
            };
        }

        public bool WriteValue<T>(ControllerAddress? address, T value)
        {
            if (!IsConnected || !TryGetChannel(address, out var ch))
                return false;

            try
            {
                var boolVal = Convert.ToBoolean(value);
                GetPortAndBit(ch, out var port, out var bit);
                var current = _daq!.ReadOutputPort(port);
                if (boolVal)
                    current |= (byte)(1 << bit);
                else
                    current &= (byte)~(1 << bit);
                _daq!.WritePort(port, current);
                return true;
            }
            catch (Exception ex)
            {
                OnException?.Invoke(this, new ExceptionEvent
                { ClassName = nameof(AdvantechPCIe1756), MethodName = nameof(WriteValue), ErrorMessage = ex.Message });
                return false;
            }
        }

        public (bool success, object[] values) ReadValues<T>(ControllerAddress[]? addresses, ushort length)
        {
            if (addresses == null || addresses.Length == 0 || !IsConnected)
                return (false, Array.Empty<object>());

            try
            {
                if (!TryGetChannel(addresses[0], out var startCh))
                    return (false, Array.Empty<object>());

                var template = addresses[0];
                var results = new object[length];
                for (int i = 0; i < length && (startCh + i) <= MaxChannel; i++)
                {
                    var (success, val) = ReadValue(new ControllerAddress
                    {
                        Address = (startCh + i).ToString(),
                        Direction = template.Direction,
                        AddressType = template.AddressType
                    });
                    results[i] = success ? val : false;
                }
                return (true, results);
            }
            catch (Exception ex)
            {
                OnException?.Invoke(this, new ExceptionEvent
                { ClassName = nameof(AdvantechPCIe1756), MethodName = nameof(ReadValues), ErrorMessage = ex.Message });
                return (false, Array.Empty<object>());
            }
        }

        public bool WriteValues<T>(ControllerAddress[]? addresses, T[]? values)
        {
            if (addresses == null || values == null || !IsConnected)
                return false;

            try
            {
                if (!TryGetChannel(addresses[0], out var startCh))
                    return false;

                for (int i = 0; i < values.Length && (startCh + i) <= MaxChannel; i++)
                {
                    if (!WriteValue(new ControllerAddress { Address = (startCh + i).ToString() }, values[i]))
                        return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                OnException?.Invoke(this, new ExceptionEvent
                { ClassName = nameof(AdvantechPCIe1756), MethodName = nameof(WriteValues), ErrorMessage = ex.Message });
                return false;
            }
        }

        // ========== IDigitalIo ==========

        public bool ReadDigitalInput(ControllerAddress channel, out bool value)
        {
            value = false;
            var (success, val) = ReadValue(channel);
            if (success)
                value = (bool)val;
            return success;
        }

        public bool ReadAllDigitalInputs(ControllerAddress[] startRegister, int count, out bool[] values)
        {
            values = Array.Empty<bool>();
            var (success, vals) = ReadValues<bool>(startRegister, (ushort)count);
            if (success)
                values = vals.Cast<bool>().ToArray();
            return success;
        }

        public bool WriteDigitalOutput(ControllerAddress channel, bool value)
        {
            return WriteValue(channel, value);
        }

        public bool WriteAllDigitalOutputs(ControllerAddress[] startRegister, bool[] values)
        {
            return WriteValues(startRegister, values);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Disconnect();
                _disposed = true;
            }
        }
    }
}
