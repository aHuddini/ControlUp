using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace ControlUp.Common
{
    /// <summary>Windows HID/SetupAPI wrapper for game controller enumeration.</summary>
    public static class DirectInputWrapper
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public uint cbSize;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct SP_DEVICE_INTERFACE_DETAIL_DATA
        {
            public uint cbSize;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string DevicePath;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, uint DeviceInterfaceDetailDataSize, ref uint RequiredSize, IntPtr DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        private static Guid GUID_DEVINTERFACE_HID = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");

        private const uint DIGCF_PRESENT = 0x00000002;
        private const uint DIGCF_DEVICEINTERFACE = 0x00000010;

        public static bool IsControllerConnected()
        {
            try
            {
                var controllers = GetConnectedControllers();
                if (controllers.Count == 0) return false;

                // Require at least one recognized controller
                return controllers.Any(c =>
                    c.Contains("Xbox") || c.Contains("Wireless") ||
                    c.Contains("VID_045E") || c.Contains("VID_054C"));
            }
            catch
            {
                return false;
            }
        }

        public static List<string> GetConnectedControllerNames()
        {
            try
            {
                var controllers = GetConnectedControllers();

                for (int i = 0; i < controllers.Count; i++)
                {
                    string controller = controllers[i];
                    string controllerLower = controller.ToLowerInvariant();

                    if (controllerLower.Contains("hid device") &&
                        (controllerLower.Contains("00001812") ||
                         (controllerLower.Contains("vid:{") && controllerLower.Contains("pid:dev"))))
                    {
                        if (IsLikelyXboxController(controller))
                        {
                            controllers[i] = "Xbox Wireless Controller (Bluetooth)";
                        }
                    }
                }

                return controllers;
            }
            catch
            {
                return new List<string> { "Error retrieving controller names" };
            }
        }

        private static bool IsLikelyXboxController(string deviceInfo)
        {
            string lower = deviceInfo.ToLowerInvariant();
            return lower.Contains("00001812") ||
                   (lower.Contains("vid:{") && lower.Contains("pid:dev"));
        }

        public static List<string> GetConnectedControllers()
        {
            var controllers = new List<string>();
            IntPtr deviceInfoSet = IntPtr.Zero;

            try
            {
                deviceInfoSet = SetupDiGetClassDevs(ref GUID_DEVINTERFACE_HID, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
                if (deviceInfoSet == IntPtr.Zero) return controllers;

                SP_DEVICE_INTERFACE_DATA deviceInterfaceData = new SP_DEVICE_INTERFACE_DATA();
                deviceInterfaceData.cbSize = (uint)Marshal.SizeOf(deviceInterfaceData);

                uint memberIndex = 0;

                while (SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref GUID_DEVINTERFACE_HID, memberIndex, ref deviceInterfaceData))
                {
                    uint requiredSize = 0;
                    SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref deviceInterfaceData, IntPtr.Zero, 0, ref requiredSize, IntPtr.Zero);

                    if (requiredSize > 0)
                    {
                        IntPtr detailDataPtr = Marshal.AllocHGlobal((int)requiredSize);
                        try
                        {
                            Marshal.WriteInt32(detailDataPtr, 0, 4 + Marshal.SystemDefaultCharSize);

                            if (SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref deviceInterfaceData, detailDataPtr, requiredSize, ref requiredSize, IntPtr.Zero))
                            {
                                SP_DEVICE_INTERFACE_DETAIL_DATA detailData = (SP_DEVICE_INTERFACE_DETAIL_DATA)Marshal.PtrToStructure(detailDataPtr, typeof(SP_DEVICE_INTERFACE_DETAIL_DATA));

                                if (IsGameControllerDevicePath(detailData.DevicePath))
                                {
                                    controllers.Add(ExtractDeviceName(detailData.DevicePath));
                                }
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(detailDataPtr);
                        }
                    }

                    memberIndex++;
                }
            }
            catch
            {
                // Enumeration failed
            }
            finally
            {
                if (deviceInfoSet != IntPtr.Zero)
                {
                    SetupDiDestroyDeviceInfoList(deviceInfoSet);
                }
            }

            return controllers;
        }

        private static bool IsGameControllerDevicePath(string devicePath)
        {
            string lowerPath = devicePath.ToLowerInvariant();

            // Xbox controllers (USB)
            bool isXboxController = lowerPath.Contains("vid_045e") && (
                lowerPath.Contains("pid_0202") || lowerPath.Contains("pid_0285") ||
                lowerPath.Contains("pid_0289") || lowerPath.Contains("pid_028f") ||
                lowerPath.Contains("pid_02a0") || lowerPath.Contains("pid_02a1") ||
                lowerPath.Contains("pid_02d1") || lowerPath.Contains("pid_02dd") ||
                lowerPath.Contains("pid_02e0") || lowerPath.Contains("pid_02e3") ||
                lowerPath.Contains("pid_02ea") || lowerPath.Contains("pid_02fd") ||
                lowerPath.Contains("pid_02ff") || lowerPath.Contains("pid_0b00") ||
                lowerPath.Contains("pid_0b05") || lowerPath.Contains("pid_0b12") ||
                lowerPath.Contains("pid_0b13"));

            // PlayStation controllers (USB)
            bool isPlayStationController = lowerPath.Contains("vid_054c") && (
                lowerPath.Contains("pid_05c4") || lowerPath.Contains("pid_09cc") ||
                lowerPath.Contains("pid_0ba0"));

            // Bluetooth patterns
            bool isBluetoothController =
                lowerPath.Contains("xbox") || lowerPath.Contains("wireless") ||
                (lowerPath.Contains("045e") && lowerPath.Contains("vid:{")) ||
                lowerPath.Contains("00001812-0000-1000-8000-00805f9b34fb") ||
                lowerPath.Contains("dualshock") || lowerPath.Contains("dualsense") ||
                (lowerPath.Contains("054c") && lowerPath.Contains("vid:{"));

            // Other brands
            bool isOtherGameController =
                lowerPath.Contains("vid_0e6f") ||  // PDP
                lowerPath.Contains("vid_12bd") ||  // GameSir
                lowerPath.Contains("vid_1532") ||  // Razer
                lowerPath.Contains("vid_2dc8") ||  // 8BitDo
                lowerPath.Contains("vid_20d6") ||  // PowerA
                lowerPath.Contains("vid_2563");    // Mayflash

            // Name patterns
            bool hasControllerName =
                lowerPath.Contains("xbox") || lowerPath.Contains("dualshock") ||
                lowerPath.Contains("dualsense") || lowerPath.Contains("elite") ||
                lowerPath.Contains("wireless") || lowerPath.Contains("gamepad");

            return isXboxController || isPlayStationController || isBluetoothController ||
                   isOtherGameController || hasControllerName;
        }

        private static string ExtractDeviceName(string devicePath)
        {
            try
            {
                string lowerPath = devicePath.ToLowerInvariant();

                // Bluetooth format
                if (lowerPath.Contains("vid:{") && lowerPath.Contains("pid:"))
                {
                    if (lowerPath.Contains("xbox") || lowerPath.Contains("wireless") || lowerPath.Contains("045e"))
                        return "Xbox Wireless Controller (Bluetooth)";
                    if (lowerPath.Contains("054c"))
                        return "PlayStation Controller (Bluetooth)";

                    int vidStart = lowerPath.IndexOf("vid:{");
                    int vidEnd = lowerPath.IndexOf("}", vidStart);
                    if (vidStart >= 0 && vidEnd > vidStart)
                    {
                        string vid = devicePath.Substring(vidStart + 5, vidEnd - vidStart - 5);
                        if (vid.Contains("00001812-0000-1000-8000-00805f9b34fb"))
                            return "Xbox Wireless Controller (Bluetooth)";
                    }
                }

                // USB format
                string[] parts = devicePath.Split('#');
                if (parts.Length >= 3)
                {
                    string vidPid = parts[1];
                    if (vidPid.Contains("&"))
                    {
                        string[] vidPidParts = vidPid.Split('&');
                        if (vidPidParts.Length >= 2)
                        {
                            string vid = vidPidParts[0].ToUpper();
                            string pid = vidPidParts[1].ToUpper();

                            if (vid == "VID_045E")
                            {
                                if (pid == "PID_0B12" || pid == "PID_0B13" || pid == "PID_0B05" ||
                                    pid == "PID_02FD" || pid == "PID_02FF")
                                    return "Xbox Wireless Controller (USB)";
                                return "Xbox Controller (USB)";
                            }
                            if (vid == "VID_054C")
                                return "PlayStation Controller (USB)";

                            return $"HID Device (VID:{vid}, PID:{pid})";
                        }
                    }
                    return $"HID Device ({vidPid})";
                }
            }
            catch
            {
                // Parsing failed
            }

            return "Unknown HID Game Controller";
        }
    }
}
