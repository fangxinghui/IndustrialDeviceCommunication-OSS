using System;
using System.Collections.Generic;
using DeviceCommunication.Abstractions;
using DeviceCommunication.Models;

namespace DeviceCommunication.Infrastructure
{
    public static class UsbVisaResourceDiscovery
    {
        public static IReadOnlyList<UsbVisaDeviceInfo> FindUsbInstruments()
        {
#if NET9_0_OR_GREATER
            if (!VisaRuntime.TryEnsureAvailable(out var error))
                throw new InvalidOperationException(error);

            return UsbVisaSessionHandle.FindUsbInstruments();
#else
            throw new PlatformNotSupportedException("USB VISA discovery requires the net9.0 target.");
#endif
        }

        public static UsbVisaDeviceInfo ParseResourceString(string resourceString)
        {
            var info = new UsbVisaDeviceInfo
            {
                ResourceString = resourceString
            };

            var parts = resourceString.Split(new[] { "::" }, StringSplitOptions.None);
            if (parts.Length >= 3)
            {
                info.VendorId = parts[1];
                info.ProductId = parts[2];
            }

            if (parts.Length >= 5)
                info.SerialNumber = parts[3];

            return info;
        }
    }
}
