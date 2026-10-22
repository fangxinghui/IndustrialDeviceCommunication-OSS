using System;
using System.Runtime.InteropServices;

namespace DeviceCommunication.Protocols.CanBus.ZLG
{
    /// <summary>
    /// Declarations for the zlgcan entry points and structs this library calls.
    /// Layout is pack-1 so buffer strides match the C ABI.
    /// </summary>
    public static class ZLGCAN
    {
        public const uint ZCAN_PCI9810 = 2;
        public const uint ZCAN_USBCAN1 = 3;
        public const uint ZCAN_USBCAN2 = 4;
        public const uint ZCAN_PCI9820 = 5;
        public const uint ZCAN_CANETUDP = 12;
        public const uint ZCAN_PCI9840 = 14;
        public const uint ZCAN_PCI9820I = 16;
        public const uint ZCAN_CANETTCP = 17;
        public const uint ZCAN_PCI5010U = 19;
        public const uint ZCAN_USBCAN_E_U = 20;
        public const uint ZCAN_USBCAN_2E_U = 21;
        public const uint ZCAN_PCI5020U = 22;
        public const uint ZCAN_PCIE9221 = 24;
        public const uint ZCAN_WIFICAN_TCP = 25;
        public const uint ZCAN_WIFICAN_UDP = 26;
        public const uint ZCAN_PCIe9120 = 27;
        public const uint ZCAN_PCIe9110 = 28;
        public const uint ZCAN_PCIe9140 = 29;
        public const uint ZCAN_USBCAN_4E_U = 31;
        public const uint ZCAN_CANDTU_200UR = 32;
        public const uint ZCAN_USBCAN_8E_U = 34;
        public const uint ZCAN_CANDTU_NET = 36;
        public const uint ZCAN_CANDTU_100UR = 37;
        public const uint ZCAN_PCIE_CANFD_200U = 39;
        public const uint ZCAN_PCIE_CANFD_400U = 40;
        public const uint ZCAN_USBCANFD_200U = 41;
        public const uint ZCAN_USBCANFD_100U = 42;
        public const uint ZCAN_USBCANFD_MINI = 43;
        public const uint ZCAN_CANSCOPE = 45;
        public const uint ZCAN_CLOUD = 46;
        public const uint ZCAN_CANDTU_NET_400 = 47;
        public const uint ZCAN_CANFDNET_TCP = 48;
        public const uint ZCAN_CANFDNET_UDP = 49;
        public const uint ZCAN_CANFDWIFI_TCP = 50;
        public const uint ZCAN_CANFDWIFI_UDP = 51;
        public const uint ZCAN_CANFDNET_400U_TCP = 52;
        public const uint ZCAN_CANFDNET_400U_UDP = 53;
        public const uint ZCAN_CANFDNET_100U_TCP = 55;
        public const uint ZCAN_CANFDNET_100U_UDP = 56;
        public const uint ZCAN_CANFDNET_800U_TCP = 57;
        public const uint ZCAN_CANFDNET_800U_UDP = 58;
        public const uint ZCAN_USBCANFD_800U = 59;
        public const uint ZCAN_PCIE_CANFD_100U_EX = 60;
        public const uint ZCAN_PCIE_CANFD_400U_EX = 61;
        public const uint ZCAN_PCIE_CANFD_200U_MINI = 62;
        public const uint ZCAN_PCIE_CANFD_200U_EX = 63;
        public const uint ZCAN_CANFDDTU_400_TCP = 64;
        public const uint ZCAN_CANFDDTU_400_UDP = 65;
        public const uint ZCAN_CANFDWIFI_200U_TCP = 66;
        public const uint ZCAN_CANFDWIFI_200U_UDP = 67;
        public const uint ZCAN_CANFDDTU_800ER_TCP = 68;
        public const uint ZCAN_CANFDDTU_800ER_UDP = 69;
        public const uint ZCAN_CANFDDTU_800EWGR_TCP = 70;
        public const uint ZCAN_CANFDDTU_800EWGR_UDP = 71;
        public const uint ZCAN_CANFDDTU_600EWGR_TCP = 72;
        public const uint ZCAN_CANFDDTU_600EWGR_UDP = 73;
        public const uint ZCAN_CANFDDTU_CASCADE_TCP = 74;
        public const uint ZCAN_CANFDDTU_CASCADE_UDP = 75;
        public const uint ZCAN_USBCANFD_400U = 76;
        public const uint ZCAN_CANFDDTU_200U = 77;
        public const uint ZCAN_ZPSCANFD_TCP = 78;
        public const uint ZCAN_ZPSCANFD_USB = 79;
        public const uint ZCAN_CANFDBRIDGE_PLUS = 80;
        public const uint ZCAN_CANFDDTU_300U = 81;
        public const uint ZCAN_PCIE_CANFD_800U = 82;
        public const uint ZCAN_PCIE_CANFD_1200U = 83;
        public const uint ZCAN_MINI_PCIE_CANFD = 84;
        public const uint ZCAN_USBCANFD_800H = 85;

        private const string LibraryName = ".\\zlgcan.dll";

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall)]
        public static extern IntPtr ZCAN_OpenDevice(uint deviceType, uint deviceIndex, uint reserved);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_CloseDevice(IntPtr deviceHandle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall)]
        public static extern IntPtr ZCAN_InitCAN(IntPtr deviceHandle, uint channelIndex, IntPtr initConfig);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_StartCAN(IntPtr channelHandle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_ResetCAN(IntPtr channelHandle);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_GetReceiveNum(IntPtr channelHandle, byte frameKind);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_Receive(IntPtr channelHandle, IntPtr buffer, uint capacity, int waitTimeMs);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_ReceiveFD(IntPtr channelHandle, IntPtr buffer, uint capacity, int waitTimeMs);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_Transmit(IntPtr channelHandle, IntPtr frames, uint count);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_TransmitFD(IntPtr channelHandle, IntPtr frames, uint count);

        [DllImport(LibraryName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        public static extern uint ZCAN_SetValue(IntPtr deviceHandle, string path, string value);

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ZCAN_CHANNEL_INIT_CONFIG
        {
            public uint can_type;
            public ChannelInitUnion config;
        }

        [StructLayout(LayoutKind.Explicit, Pack = 1)]
        public struct ChannelInitUnion
        {
            [FieldOffset(0)]
            public CanFdInit canfd;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct CanFdInit
        {
            public uint acc_code;
            public uint acc_mask;
            public uint abit_timing;
            public uint dbit_timing;
            public uint brp;
            public byte filter;
            public byte mode;
            public ushort pad;
            public uint reserved;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct can_frame
        {
            public uint can_id;
            public byte can_dlc;
            public byte __pad;
            public byte __res0;
            public byte __res1;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] data;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct canfd_frame
        {
            public uint can_id;
            public byte len;
            public byte flags;
            public byte __res0;
            public byte __res1;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public byte[] data;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ZCAN_Transmit_Data
        {
            public can_frame frame;
            public uint transmit_type;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ZCAN_TransmitFD_Data
        {
            public canfd_frame frame;
            public uint transmit_type;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ZCAN_Receive_Data
        {
            public can_frame frame;
            public ulong timestamp;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ZCAN_ReceiveFD_Data
        {
            public canfd_frame frame;
            public ulong timestamp;
        }
    }
}
