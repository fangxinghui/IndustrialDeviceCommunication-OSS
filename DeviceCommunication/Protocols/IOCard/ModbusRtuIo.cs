using System;
using System.Linq;
using CoreShared;
using CoreShared.Events;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Attributes;
using DeviceCommunication.Models;
using DeviceCommunication.Protocols;
using IConfiguration = DeviceCommunication.Abstractions.IConfiguration;

namespace DeviceCommunication.Protocols.IOCard
{
    [ControllerDriver(brand: "*", model: "*", controllerType: ControllerType.DigitalIoModule, IsDefault = true)]
    public class ModbusRtuIo : IDigitalIo, IController, IModbusBusPollScope, IModbusSharedBusIdentity
    {
        private readonly ControllerConfiguration _sourceConfiguration;

        public event EventHandler<ExceptionEvent>? OnException;
        public event EventHandler<ExecuteLogEvent>? OnExecuteLog;
        protected virtual IController? Controller { get; private set; }

        public bool IsConnected => Controller?.IsConnected ?? false;

        public string LogicalName =>
            ModbusBusControllerFacade.GetLogicalName(Controller, _sourceConfiguration.Description);

        public byte SlaveId => ModbusBusControllerFacade.GetSlaveId(Controller);

        public string BusKey => ModbusBusControllerFacade.GetBusKey(Controller);

        public IDisposable EnterPollScope() => ModbusBusControllerFacade.EnterPollScope(Controller);

        public ModbusRtuIo(ControllerConfiguration controllerConfiguration)
        {
            _sourceConfiguration = controllerConfiguration;
            try
            {
                Controller = ModbusConfigurationBuilder.CreateProtocolController(controllerConfiguration);
                WireControllerEvents(Controller);
            }
            catch (Exception e)
            {
                OnException?.Invoke(this, new ExceptionEvent { MethodName = nameof(ModbusRtuIo), ClassName = nameof(ModbusRtuIo), ErrorMessage = e.Message });
            }
        }

        public IConfiguration? CreateConfiguration()
            => ModbusConfigurationBuilder.TryBuild(_sourceConfiguration, out var configuration) ? configuration : null;

        public bool Initialize<T>(T parameter) => Controller?.Initialize(parameter) ?? false;

        public bool Connect(int timeout) => Controller?.Connect(timeout) ?? false;

        public void Disconnect() => Controller?.Disconnect();

        public (bool success, object value) ReadValue(ControllerAddress? address)
            => Controller?.ReadValue(address) ?? (false, null!);

        public bool WriteValue<T>(ControllerAddress? address, T value)
            => Controller?.WriteValue(address, value) ?? false;

        public (bool success, object[] values) ReadValues<T>(ControllerAddress[]? addresses, ushort length)
            => Controller?.ReadValues<T>(addresses, length) ?? (false, Array.Empty<object>());

        public bool WriteValues<T>(ControllerAddress[]? addresses, T[]? values)
            => Controller?.WriteValues(addresses, values) ?? false;

        private void WireControllerEvents(IController? controller)
        {
            if (controller is null) return;
            controller.OnException += (_, e) => OnException?.Invoke(this, e);
            controller.OnExecuteLog += (_, e) => OnExecuteLog?.Invoke(this, e);
        }
        /// <summary>
        /// 设置线圈输出
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool WriteDigitalOutput(ControllerAddress channel, bool value)
        {
            try
            {
                if(Controller is null)
                {
                    OnExecuteLog?.Invoke(this, new ExecuteLogEvent { MethodName = nameof(WriteDigitalOutput), ClassName = nameof(ModbusRtuIo), LogMessage = "Controller is null." });
                    return false;
                }

                // 进入 PollScope 持锁：确保本设备的写操作不会被同一串口上其他设备的 poll 打断
                // 锁释放后才轮到其他设备操作总线，保证 RS485 半双工互斥
                using var pollScope = EnterPollScope();

                return Controller.WriteValue<bool>(channel,value);
            }
            catch (Exception e)
            {
                OnException?.Invoke(this, new ExceptionEvent { MethodName = nameof(WriteDigitalOutput), ClassName = nameof(ModbusRtuIo), ErrorMessage = e.Message });
                return false;
            }
        }
        /// <summary>
        /// 写多个线圈
        /// </summary>
        /// <param name="startRegister"></param>
        /// <param name="values"></param>
        /// <returns></returns>
        public bool WriteAllDigitalOutputs(ControllerAddress[] startRegister, bool[] values)
        {
            try
            {
                if (Controller is null)
                {
                    OnExecuteLog?.Invoke(this, new ExecuteLogEvent { MethodName = nameof(WriteAllDigitalOutputs), ClassName = nameof(ModbusRtuIo), LogMessage = "Controller is null." });
                    return false;
                }

                using var pollScope = EnterPollScope();
                return Controller.WriteValues<bool>(startRegister, values);
            }
            catch (Exception e)
            {
                OnException?.Invoke(this, new ExceptionEvent { MethodName = nameof(WriteAllDigitalOutputs), ClassName = nameof(ModbusRtuIo), ErrorMessage = e.Message });
                return false;
            }
        }
        /// <summary>
        /// 读取单个输入寄存器的值
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool ReadDigitalInput(ControllerAddress channel, out bool value)
        {
            value = false;
            try
            {
                if (Controller is null)
                {
                    OnExecuteLog?.Invoke(this, new ExecuteLogEvent { MethodName = nameof(ReadDigitalInput), ClassName = nameof(ModbusRtuIo), LogMessage = "Controller is null." });
                    return false;
                }

                using var pollScope = EnterPollScope();
                var result=Controller.ReadValue(channel);
                if(result is { success: true, value: bool boolValue })
                {
                    value = boolValue;
                }
                    
                return result.success;
            }
            catch (Exception e)
            {
                OnException?.Invoke(this, new ExceptionEvent { MethodName = nameof(ReadDigitalInput), ClassName = nameof(ModbusRtuIo), ErrorMessage = e.Message });
                return false;
            }
        }
        /// <summary>
        /// 读取多个输入寄存器的值
        /// </summary>
        /// <param name="startRegister"></param>
        /// <param name="count"></param>
        /// <param name="values"></param>
        /// <returns></returns>
        public bool ReadAllDigitalInputs(ControllerAddress[] startRegister, int count, out bool[] values)
        {
            values = Array.Empty<bool>();
            try
            {
                if(Controller is null)
                {
                    OnExecuteLog?.Invoke(this, new ExecuteLogEvent { MethodName = nameof(ReadAllDigitalInputs), ClassName = nameof(ModbusRtuIo), LogMessage = "Controller is null." });
                    return false;
                }

                using var pollScope = EnterPollScope();
                var result = Controller.ReadValues<bool[]>(startRegister, (ushort)count);
                if(result is { success: true, values: { } })
                {
                    foreach (var value in result.values)
                    {
                        if(value is bool boolValue)
                        {
                            values = values.Append(boolValue).ToArray();
                        }
                    }
                }
                return result.success;
            }
            catch (Exception e)
            {
                OnException?.Invoke(this, new ExceptionEvent { MethodName = nameof(ReadAllDigitalInputs), ClassName = nameof(ModbusRtuIo), ErrorMessage = e.Message });
                return false;
            }
        }


    }
}