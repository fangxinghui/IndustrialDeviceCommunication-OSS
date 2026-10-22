using DeviceCommunication.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace DeviceCommunication.Models
{
    public class OmronNetConfiguration: IConfiguration
    {
        /// <summary>
        /// PLC IP地址
        /// </summary>
        public string? IP { get; set; }
        public int Port { get; set; }
        public byte ComputerSA1 { get; set; }
        public byte PLCDA2 { get; set; }
        public ModbusDataType DataFormat { get; set; }
    }
}
