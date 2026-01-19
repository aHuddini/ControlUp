using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ControlUp.Common
{
    /// <summary>
    /// Direct wrapper for Windows HID/DirectInput API to detect all game controllers
    /// </summary>
    public static class DirectInputWrapper
    {
        // Windows API structures and functions for device enumeration
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

        // GUIDs for different device classes
        private static Guid GUID_DEVINTERFACE_HID = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");
        private static Guid GUID_DEVCLASS_HIDCLASS = new Guid("745A17A0-74D3-11D0-B6FE-00A0C90F57DA");

        private const uint DIGCF_PRESENT = 0x00000002;
        private const uint DIGCF_DEVICEINTERFACE = 0x00000010;

        /// <summary>
        /// Checks if any HID game controllers are currently connected
        /// </summary>
        public static bool IsControllerConnected()
        {
            try
            {
                List<string> connectedControllers = GetConnectedControllers();
                if (connectedControllers.Count > 0)
                {
                    Console.WriteLine($"ControlUp: Found {connectedControllers.Count} HID controllers:");
                    foreach (var controller in connectedControllers)
                    {
                        Console.WriteLine($"  - {controller}");
                    }

                    // Additional validation: ensure we have at least one controller with a known VID/PID
                    bool hasKnownController = connectedControllers.Any(c =>
                        c.Contains("Xbox") || c.Contains("Wireless") ||
                        c.Contains("VID_045E") || c.Contains("VID_054C"));

                    if (hasKnownController)
                    {
                        Console.WriteLine("ControlUp: Confirmed known game controller detected");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine("ControlUp: Only unknown HID devices detected, not counting as game controllers");
                        return false;
                    }
                }
                else
                {
                    Console.WriteLine("ControlUp: No HID controllers found");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ControlUp: Error enumerating HID devices: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets a list of connected controller names for display purposes
        /// </summary>
        public static List<string> GetConnectedControllerNames()
        {
            try
            {
                var controllers = GetConnectedControllers();

                // Enhance the controller names with better Xbox identification
                for (int i = 0; i < controllers.Count; i++)
                {
                    string controller = controllers[i];
                    string controllerLower = controller.ToLowerInvariant();

                    // Check for Xbox-specific patterns
                    if (controllerLower.Contains("hid device") &&
                        (controllerLower.Contains("00001812") || // HID Service UUID
                         controllerLower.Contains("vid:{") && controllerLower.Contains("pid:dev"))) // Bluetooth HID format
                    {
                        // This is likely an Xbox Wireless Controller
                        // Check if we can get more specific information
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

        /// <summary>
        /// Checks if a device string represents a likely Xbox controller
        /// </summary>
        private static bool IsLikelyXboxController(string deviceInfo)
        {
            string lower = deviceInfo.ToLowerInvariant();

            // Xbox controllers typically have these characteristics:
            // 1. HID Service UUID (00001812-0000-1000-8000-00805f9b34fb)
            // 2. Bluetooth VID/PID format with DEV as PID
            // 3. May not have "xbox" in the name but Windows recognizes them as such

            return lower.Contains("00001812") || // HID Service UUID
                   (lower.Contains("vid:{") && lower.Contains("pid:dev")); // Bluetooth HID format
        }

        /// <summary>
        /// Gets a list of connected HID game controllers
        /// </summary>
        public static List<string> GetConnectedControllers()
        {
            List<string> controllers = new List<string>();
            IntPtr deviceInfoSet = IntPtr.Zero;

            try
            {
                // Get device info set for HID devices
                deviceInfoSet = SetupDiGetClassDevs(ref GUID_DEVINTERFACE_HID, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);

                if (deviceInfoSet == IntPtr.Zero)
                {
                    Console.WriteLine("ControlUp: Failed to get HID device info set");
                    return controllers;
                }

                SP_DEVICE_INTERFACE_DATA deviceInterfaceData = new SP_DEVICE_INTERFACE_DATA();
                deviceInterfaceData.cbSize = (uint)Marshal.SizeOf(deviceInterfaceData);

                uint memberIndex = 0;

                // Enumerate all HID devices
                while (SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref GUID_DEVINTERFACE_HID, memberIndex, ref deviceInterfaceData))
                {
                    // Get device interface detail
                    uint requiredSize = 0;
                    SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref deviceInterfaceData, IntPtr.Zero, 0, ref requiredSize, IntPtr.Zero);

                    if (requiredSize > 0)
                    {
                        IntPtr detailDataPtr = Marshal.AllocHGlobal((int)requiredSize);
                        try
                        {
                            Marshal.WriteInt32(detailDataPtr, 0, 4 + Marshal.SystemDefaultCharSize); // cbSize

                            if (SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref deviceInterfaceData, detailDataPtr, requiredSize, ref requiredSize, IntPtr.Zero))
                            {
                                SP_DEVICE_INTERFACE_DETAIL_DATA detailData = (SP_DEVICE_INTERFACE_DETAIL_DATA)Marshal.PtrToStructure(detailDataPtr, typeof(SP_DEVICE_INTERFACE_DETAIL_DATA));

                                // Check if this is a game controller (look for common patterns)
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
            catch (Exception ex)
            {
                Console.WriteLine($"ControlUp: Error in GetConnectedControllers: {ex.Message}");
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

        /// <summary>
        /// Checks if a device path represents a game controller
        /// </summary>
        private static bool IsGameControllerDevicePath(string devicePath)
        {
            string lowerPath = devicePath.ToLowerInvariant();

            // Known Xbox controller VID/PID combinations (USB)
            bool isXboxController = lowerPath.Contains("vid_045e") && (
                lowerPath.Contains("pid_0202") || // Xbox Controller S
                lowerPath.Contains("pid_0285") || // Xbox Controller S
                lowerPath.Contains("pid_0289") || // Xbox Controller S
                lowerPath.Contains("pid_028f") || // Xbox Controller S
                lowerPath.Contains("pid_02a0") || // Xbox Controller S
                lowerPath.Contains("pid_02a1") || // Xbox Controller S
                lowerPath.Contains("pid_02d1") || // Xbox One Controller
                lowerPath.Contains("pid_02dd") || // Xbox One Controller (firmware 2015)
                lowerPath.Contains("pid_02e0") || // Xbox One Controller (firmware 2015)
                lowerPath.Contains("pid_02e3") || // Xbox One Elite Controller
                lowerPath.Contains("pid_02ea") || // Xbox One S Controller
                lowerPath.Contains("pid_02fd") || // Xbox One S Controller (Bluetooth)
                lowerPath.Contains("pid_02ff") || // Xbox One Elite Controller
                lowerPath.Contains("pid_0b00") || // Xbox Elite Wireless Controller
                lowerPath.Contains("pid_0b05") || // Xbox Elite Wireless Controller
                lowerPath.Contains("pid_0b12") || // Xbox Wireless Controller
                lowerPath.Contains("pid_0b13")    // Xbox Wireless Controller
            );

            // Known PlayStation controller VID/PID combinations (USB)
            bool isPlayStationController = lowerPath.Contains("vid_054c") && (
                lowerPath.Contains("pid_05c4") || // DualShock 4
                lowerPath.Contains("pid_09cc") || // DualShock 4 (v2)
                lowerPath.Contains("pid_0ba0")    // DualSense
            );

            // Xbox controllers via Bluetooth (different format)
            bool isXboxBluetoothController =
                lowerPath.Contains("xbox") ||
                lowerPath.Contains("wireless") ||
                (lowerPath.Contains("045e") && lowerPath.Contains("vid:{")) || // Microsoft Xbox VID in Bluetooth format
                lowerPath.Contains("00001812-0000-1000-8000-00805f9b34fb"); // HID Service UUID commonly used by Xbox controllers

            // PlayStation controllers via Bluetooth
            bool isPlayStationBluetoothController =
                lowerPath.Contains("dualshock") ||
                lowerPath.Contains("dualsense") ||
                (lowerPath.Contains("054c") && lowerPath.Contains("vid:{")); // Sony VID in Bluetooth format

            // Other known game controller brands (USB)
            bool isOtherGameController =
                lowerPath.Contains("vid_0e6f") || // PDP
                lowerPath.Contains("vid_12bd") || // GameSir
                lowerPath.Contains("vid_1532") || // Razer
                lowerPath.Contains("vid_2dc8") || // 8BitDo
                lowerPath.Contains("vid_20d6") || // PowerA
                lowerPath.Contains("vid_2563")    // Shenzhen; // Mayflash, etc.
                ;

            // Specific device name patterns (more reliable than generic terms)
            bool hasSpecificControllerName =
                lowerPath.Contains("xbox") ||
                lowerPath.Contains("dualshock") ||
                lowerPath.Contains("dualsense") ||
                lowerPath.Contains("elite") ||
                lowerPath.Contains("wireless") ||
                lowerPath.Contains("gamepad");

            return isXboxController || isPlayStationController || isXboxBluetoothController ||
                   isPlayStationBluetoothController || isOtherGameController || hasSpecificControllerName;
        }

        /// <summary>
        /// Extracts a readable device name from the device path
        /// </summary>
        private static string ExtractDeviceName(string devicePath)
        {
            try
            {
                string lowerPath = devicePath.ToLowerInvariant();

                // Handle Bluetooth device paths (different format)
                if (lowerPath.Contains("vid:{") && lowerPath.Contains("pid:"))
                {
                    // Bluetooth format: VID:{uuid}, PID:dev
                    int vidStart = lowerPath.IndexOf("vid:{");
                    int vidEnd = lowerPath.IndexOf("}", vidStart);
                    int pidStart = lowerPath.IndexOf("pid:");

                    if (vidStart >= 0 && vidEnd > vidStart && pidStart > vidEnd)
                    {
                        string vid = devicePath.Substring(vidStart + 5, vidEnd - vidStart - 5);
                        string pid = devicePath.Substring(pidStart + 4);

                        // Check if this is a known Xbox controller by VID/PID or device name
                        if (lowerPath.Contains("xbox") || lowerPath.Contains("wireless") ||
                            lowerPath.Contains("045e")) // Microsoft Xbox VID
                        {
                            return "Xbox Wireless Controller (Bluetooth)";
                        }
                        else if (lowerPath.Contains("054c")) // Sony PlayStation VID
                        {
                            return "PlayStation Controller (Bluetooth)";
                        }
                        else
                        {
                            // Check if this looks like an Xbox controller based on common Bluetooth patterns
                            // The HID Service UUID (00001812-0000-1000-8000-00805f9b34fb) is common for game controllers
                            if (vid.Contains("00001812-0000-1000-8000-00805f9b34fb"))
                            {
                                // This is likely an Xbox Wireless Controller based on the HID service UUID
                                // and the fact that we see this pattern with Xbox controllers
                                return "Xbox Wireless Controller (Bluetooth)";
                            }
                            else
                            {
                                return $"Bluetooth HID Device (VID:{vid}, PID:{pid})";
                            }
                        }
                    }
                }

                // Handle USB device paths (standard format)
                string[] parts = devicePath.Split('#');
                if (parts.Length >= 3)
                {
                    // Format: ...#VID_PID#REV#...
                    string vidPid = parts[1];
                    if (vidPid.Contains("&"))
                    {
                        string[] vidPidParts = vidPid.Split('&');
                        if (vidPidParts.Length >= 2)
                        {
                            string vid = vidPidParts[0].ToUpper();
                            string pid = vidPidParts[1].ToUpper();

                            // Check for known Xbox controllers
                            if (vid == "VID_045E" &&
                                (pid == "PID_0B12" || pid == "PID_0B13" || pid == "PID_0B05" ||
                                 pid == "PID_02FD" || pid == "PID_02FF"))
                            {
                                return "Xbox Wireless Controller (USB)";
                            }
                            else if (vid == "VID_045E")
                            {
                                return "Xbox Controller (USB)";
                            }
                            else if (vid == "VID_054C")
                            {
                                return "PlayStation Controller (USB)";
                            }

                            return $"HID Device (VID:{vid}, PID:{pid})";
                        }
                    }
                    return $"HID Device ({vidPid})";
                }
            }
            catch
            {
                // If parsing fails, return a generic name
            }

            return "Unknown HID Game Controller";
        }
    }
}