using System;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Models;

namespace DeviceCommunication.Infrastructure
{
    public static class UsbCommPortFactory
    {
        public static ICommPort TryCreateUsb(UsbConfiguration configuration, string? resourceString = null)
        {
            if (configuration is null)
                throw new ArgumentNullException(nameof(configuration), "USB configuration cannot be null.");

            if (!VisaRuntime.IsAvailable)
                return new UnavailableVisaCommPort(VisaRuntime.UnavailableReason);

            return new UsbVisaCommPort(configuration, resourceString);
        }
    }
}
