using System;
using System.Runtime.InteropServices;

namespace ControlUp.Common
{
    /// <summary>Raw Input API wrapper for HID controller detection.</summary>
    public static class RawInputWrapper
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICELIST
        {
            public IntPtr hDevice;
            public uint dwType;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RID_DEVICE_INFO_HID
        {
            public uint dwVendorId;
            public uint dwProductId;
            public uint dwVersionNumber;
            public ushort usUsagePage;
            public ushort usUsage;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RID_DEVICE_INFO
        {
            public uint cbSize;
            public uint dwType;
            public RID_DEVICE_INFO_HID hid;
        }

        [DllImport("user32.dll")]
        private static extern uint GetRawInputDeviceList(IntPtr pRawInputDeviceList, ref uint puiNumDevices, uint cbSize);

        [DllImport("user32.dll")]
        private static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

        private const uint RIDI_DEVICEINFO = 0x2000000b;
        private const uint RIM_TYPEHID = 2;

        // HID usage constants
        private const ushort HID_USAGE_PAGE_GENERIC = 0x01;
        private const ushort HID_USAGE_JOYSTICK = 0x04;
        private const ushort HID_USAGE_GAMEPAD = 0x05;
        private const ushort HID_USAGE_MULTIAXIS = 0x08;

        public static bool IsControllerConnected()
        {
            try
            {
                uint deviceCount = 0;
                uint cbSize = (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICELIST));

                uint result = GetRawInputDeviceList(IntPtr.Zero, ref deviceCount, cbSize);
                if (result != 0 || deviceCount == 0)
                    return false;

                IntPtr deviceListPtr = Marshal.AllocHGlobal((int)(cbSize * deviceCount));

                try
                {
                    result = GetRawInputDeviceList(deviceListPtr, ref deviceCount, cbSize);
                    if (result != deviceCount)
                        return false;

                    for (uint i = 0; i < deviceCount; i++)
                    {
                        IntPtr devicePtr = IntPtr.Add(deviceListPtr, (int)(i * cbSize));
                        RAWINPUTDEVICELIST device = Marshal.PtrToStructure<RAWINPUTDEVICELIST>(devicePtr);

                        if (device.dwType == RIM_TYPEHID && IsGameController(device.hDevice))
                            return true;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(deviceListPtr);
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static bool IsGameController(IntPtr hDevice)
        {
            try
            {
                uint infoSize = 0;
                uint result = GetRawInputDeviceInfo(hDevice, RIDI_DEVICEINFO, IntPtr.Zero, ref infoSize);
                if (result != 0 || infoSize == 0)
                    return false;

                IntPtr infoPtr = Marshal.AllocHGlobal((int)infoSize);

                try
                {
                    result = GetRawInputDeviceInfo(hDevice, RIDI_DEVICEINFO, infoPtr, ref infoSize);
                    if (result == infoSize)
                    {
                        RID_DEVICE_INFO deviceInfo = Marshal.PtrToStructure<RID_DEVICE_INFO>(infoPtr);

                        if (deviceInfo.hid.usUsagePage == HID_USAGE_PAGE_GENERIC &&
                            (deviceInfo.hid.usUsage == HID_USAGE_JOYSTICK ||
                             deviceInfo.hid.usUsage == HID_USAGE_GAMEPAD ||
                             deviceInfo.hid.usUsage == HID_USAGE_MULTIAXIS))
                        {
                            return true;
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(infoPtr);
                }
            }
            catch
            {
                return false;
            }

            return false;
        }
    }
}
