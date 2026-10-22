using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using DeviceCommunication.Models;
using DeviceCommunication.Abstractions;

namespace DeviceCommunication.Infrastructure
{
    public class SerialPortFactory
    {
        public static ISerialPort TryCreateSerialPort(SerialPortConfiguration configuration)
        {
            if (configuration is null)
                    throw new ArgumentNullException(nameof(configuration), "Serial port configuration cannot be null.");
            return new SerialPortStreamAdapter(configuration);
        }
    }
   
}
