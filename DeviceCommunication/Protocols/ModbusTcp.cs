using CoreShared.Events;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Models;
using FluentModbus;
using System;
using System.Linq;
using System.Net;

namespace DeviceCommunication.Protocols
{
    internal class ModbusTcp:ModbusRtu
    {
        private ModbusTcpConfiguration? _configuration;
        private FluentModbusTcpClientAdapter? _tcpClient;
        private bool _tcpConnected;

        public override bool IsConnected => _tcpConnected;

        public override IConfiguration? CreateConfiguration() => _configuration;

        public override bool Initialize<T>(T parameter)
        {
            if (parameter is ModbusTcpConfiguration config)
            {
                _configuration = config;
                StationId = config.StationId;
                ModbusDataType = config.ModbusDataType;
                IsAddressStartWithZero = config.IsAddressStartWithZero;
                _tcpClient = new FluentModbusTcpClientAdapter();
                modbusclient = _tcpClient;
                return true;
            }
            else
            {
                return false;
            }
        }
        public override bool Connect(int timeout = 5000)
        {
            if (_tcpConnected)
                return true;

            if (_tcpClient != null)
            {
                try
                {
                    _tcpClient.ReadTimeout = timeout;
                    _tcpClient.WriteTimeout = timeout;
                    IPEndPoint iPEndPoint = new IPEndPoint(IPAddress.Parse(_configuration?.IpAddress!), _configuration.Port);
                    _tcpClient.Connect(iPEndPoint);
                    _tcpConnected = true;
                    return true;
                }
                catch (Exception)
                {
                    _tcpConnected = false;
                    return false;
                }
            }

            return false;
        }

        public override void Disconnect()
        {
            modbusclient?.Close();
            _tcpConnected = false;
        }

        private sealed class FluentModbusTcpClientAdapter : IModbusClientAdapter
        {
            private readonly ModbusTcpClient _client = new ModbusTcpClient();

            public int ReadTimeout
            {
                get => _client.ReadTimeout;
                set => _client.ReadTimeout = value;
            }

            public int WriteTimeout
            {
                get => _client.WriteTimeout;
                set => _client.WriteTimeout = value;
            }

            public void Connect(IPEndPoint endpoint)
            {
                _client.Connect(endpoint, ModbusEndianness.LittleEndian);
            }

            public void Close()
            {
                if (_client is IDisposable disposable)
                    disposable.Dispose();
            }

            public bool[] ReadCoils(byte stationId, int startAddress, ushort length) =>
                ToBoolArray(_client.ReadCoils(stationId, startAddress, length).ToArray(), length);

            public bool[] ReadDiscreteInputs(byte stationId, int startAddress, ushort length) =>
                ToBoolArray(_client.ReadDiscreteInputs(stationId, startAddress, length).ToArray(), length);

            public ushort[] ReadHoldingRegisters(byte stationId, ushort startAddress, ushort length) =>
                ToUInt16Array(_client.ReadHoldingRegisters(stationId, startAddress, length).ToArray(), length);

            public ushort[] ReadInputRegisters(byte stationId, ushort startAddress, ushort length) =>
                ToUInt16Array(_client.ReadInputRegisters(stationId, startAddress, length).ToArray(), length);

            public void WriteSingleCoil(byte stationId, int address, bool value) =>
                _client.WriteSingleCoil(stationId, address, value);

            public void WriteSingleRegister(byte stationId, int address, short value) =>
                _client.WriteSingleRegister(stationId, address, value);

            public void WriteMultipleCoils(byte stationId, int startAddress, bool[] values) =>
                _client.WriteMultipleCoils(stationId, startAddress, values);

            public void WriteMultipleRegisters(byte stationId, int startAddress, int[] values) =>
                _client.WriteMultipleRegisters(stationId, startAddress, values);

            public void WriteMultipleRegisters(byte stationId, int startAddress, short[] values) =>
                _client.WriteMultipleRegisters(stationId, startAddress, values);

            private static bool[] ToBoolArray(byte[] bytes, ushort length)
            {
                var values = new bool[length];
                for (var i = 0; i < length; i++)
                    values[i] = i < bytes.Length && bytes[i] != 0;
                return values;
            }

            private static ushort[] ToUInt16Array(byte[] bytes, ushort length)
            {
                var values = new ushort[length];
                for (var i = 0; i < length; i++)
                {
                    var offset = i * 2;
                    if (offset + 1 < bytes.Length)
                        values[i] = (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
                }
                return values;
            }
        }
    }
}