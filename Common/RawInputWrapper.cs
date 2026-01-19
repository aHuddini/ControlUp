using System;
using System.Runtime.InteropServices;
using System.Linq;

namespace ControlUp.Common
{
    /// <summary>
    /// Wrapper for Raw Input API to detect any HID game controllers
    /// </summary>
    public static class RawInputWrapper
    {
        // Raw Input structures and constants
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

        private const uint RIDI_DEVICENAME = 0x20000007;
        private const uint RIDI_DEVICEINFO = 0x2000000b;
        private const uint RIM_TYPEHID = 2;

        // Common game controller usage pages and usages
        private const ushort HID_USAGE_PAGE_GENERIC = 0x01;
        private const ushort HID_USAGE_JOYSTICK = 0x04;
        private const ushort HID_USAGE_GAMEPAD = 0x05;
        private const ushort HID_USAGE_MULTIAXIS = 0x08;

        /// <summary>
        /// Checks if any HID game controller is currently connected via Raw Input
        /// </summary>
        public static bool IsControllerConnected()
        {
            try
            {
                // Get number of devices
                uint deviceCount = 0;
                uint cbSize = (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICELIST));

                // First call to get count
                uint result = GetRawInputDeviceList(IntPtr.Zero, ref deviceCount, cbSize);
                if (result != 0 || deviceCount == 0)
                {
                    return false;
                }

                // Allocate memory for device list
                IntPtr deviceListPtr = Marshal.AllocHGlobal((int)(cbSize * deviceCount));

                try
                {
                    // Get device list
                    result = GetRawInputDeviceList(deviceListPtr, ref deviceCount, cbSize);
                    if (result != deviceCount)
                    {
                        return false;
                    }

                    // Check each device
                    for (uint i = 0; i < deviceCount; i++)
                    {
                        IntPtr devicePtr = IntPtr.Add(deviceListPtr, (int)(i * cbSize));
                        RAWINPUTDEVICELIST device = Marshal.PtrToStructure<RAWINPUTDEVICELIST>(devicePtr);

                        // Only check HID devices
                        if (device.dwType == RIM_TYPEHID)
                        {
                            if (IsGameController(device.hDevice))
                            {
                                return true;
                            }
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(deviceListPtr);
                }
            }
            catch
            {
                // Raw Input not available or failed
                return false;
            }

            return false;
        }

        private static bool IsGameController(IntPtr hDevice)
        {
            try
            {
                // Get device info size
                uint infoSize = 0;
                uint result = GetRawInputDeviceInfo(hDevice, RIDI_DEVICEINFO, IntPtr.Zero, ref infoSize);
                if (result != 0 || infoSize == 0)
                {
                    return false;
                }

                // Allocate memory for device info
                IntPtr infoPtr = Marshal.AllocHGlobal((int)infoSize);

                try
                {
                    // Get device info
                    result = GetRawInputDeviceInfo(hDevice, RIDI_DEVICEINFO, infoPtr, ref infoSize);
                    if (result == infoSize)
                    {
                        RID_DEVICE_INFO deviceInfo = Marshal.PtrToStructure<RID_DEVICE_INFO>(infoPtr);

                        // Check if it's a game controller
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