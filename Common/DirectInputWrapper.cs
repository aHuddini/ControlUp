using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace ControlUp.Common
{
    /// <summary>Windows HID/SetupAPI wrapper for game controller enumeration and input reading.</summary>
    public static class DirectInputWrapper
    {
        // Static logger for diagnostics
        public static FileLogger Logger { get; set; }
        // HID API imports for reading controller input
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(
            SafeFileHandle hFile,
            byte[] lpBuffer,
            uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead,
            IntPtr lpOverlapped);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(SafeFileHandle HidDeviceObject, out IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_FreePreparsedData(IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetCaps(IntPtr PreparsedData, out HIDP_CAPS Capabilities);

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;

        // Cached device handle for input reading
        private static SafeFileHandle _cachedDeviceHandle;
        private static string _cachedDevicePath;
        private static int _cachedReportLength;
        private static ControllerType _cachedControllerType;

        private enum ControllerType
        {
            Unknown,
            DualSenseUSB,
            DualSenseBluetooth,
            DualShock4USB,
            DualShock4Bluetooth
        }

        /// <summary>Parsed controller input state from HID report.</summary>
        public class HidControllerReading
        {
            public bool IsValid { get; set; }
            public bool Cross { get; set; }      // A equivalent
            public bool Circle { get; set; }     // B equivalent
            public bool Square { get; set; }     // X equivalent
            public bool Triangle { get; set; }   // Y equivalent
            public bool DPadUp { get; set; }
            public bool DPadDown { get; set; }
            public bool DPadLeft { get; set; }
            public bool DPadRight { get; set; }
            public bool L1 { get; set; }
            public bool R1 { get; set; }
            public bool L2 { get; set; }
            public bool R2 { get; set; }
            public bool L3 { get; set; }
            public bool R3 { get; set; }
            public bool Share { get; set; }      // Back/View equivalent
            public bool Options { get; set; }    // Start/Menu equivalent
            public bool PS { get; set; }         // Guide equivalent
            public bool Touchpad { get; set; }
            public byte LeftStickX { get; set; }  // 0-255, 128 = center
            public byte LeftStickY { get; set; }
            public byte RightStickX { get; set; }
            public byte RightStickY { get; set; }
        }

        /// <summary>Read current input state from a connected PlayStation controller via HID.</summary>
        public static HidControllerReading GetHidControllerReading()
        {
            var result = new HidControllerReading { IsValid = false };

            try
            {
                // Find a PlayStation controller if we don't have a cached handle
                if (_cachedDeviceHandle == null || _cachedDeviceHandle.IsClosed || _cachedDeviceHandle.IsInvalid)
                {
                    var path = FindPlayStationControllerPath();
                    if (string.IsNullOrEmpty(path))
                        return result;

                    OpenController(path);
                }

                if (_cachedDeviceHandle == null || _cachedDeviceHandle.IsClosed || _cachedDeviceHandle.IsInvalid)
                    return result;

                // Read HID report
                byte[] buffer = new byte[_cachedReportLength > 0 ? _cachedReportLength : 78];
                uint bytesRead;

                if (!ReadFile(_cachedDeviceHandle, buffer, (uint)buffer.Length, out bytesRead, IntPtr.Zero))
                {
                    // Read failed, close handle so we try again next time
                    CloseController();
                    return result;
                }

                if (bytesRead < 10)
                    return result;

                // Parse based on controller type
                return ParseHidReport(buffer, _cachedControllerType);
            }
            catch
            {
                CloseController();
                return result;
            }
        }

        private static string FindPlayStationControllerPath()
        {
            var devices = GetAllHidDevicePaths();
            foreach (var path in devices)
            {
                string lower = path.ToLowerInvariant();

                // Check for Sony VID (054c)
                bool isSonyUsb = lower.Contains("vid_054c");
                bool isSonyBluetooth = lower.Contains("_vid&0002054c");

                if (!isSonyUsb && !isSonyBluetooth)
                    continue;

                // Check for DualSense or DualShock 4 PIDs
                bool isDualSense = lower.Contains("pid_0ce6") || lower.Contains("_pid&0ce6") ||
                                   lower.Contains("pid_0df2") || lower.Contains("_pid&0df2");
                bool isDualShock4 = lower.Contains("pid_05c4") || lower.Contains("_pid&05c4") ||
                                    lower.Contains("pid_09cc") || lower.Contains("_pid&09cc");

                if (isDualSense || isDualShock4)
                    return path;
            }
            return null;
        }

        private static void OpenController(string path)
        {
            CloseController();

            string lower = path.ToLowerInvariant();

            // Determine controller type
            bool isBluetooth = lower.Contains("_vid&0002054c");
            bool isDualSense = lower.Contains("pid_0ce6") || lower.Contains("_pid&0ce6") ||
                               lower.Contains("pid_0df2") || lower.Contains("_pid&0df2");

            if (isDualSense)
                _cachedControllerType = isBluetooth ? ControllerType.DualSenseBluetooth : ControllerType.DualSenseUSB;
            else
                _cachedControllerType = isBluetooth ? ControllerType.DualShock4Bluetooth : ControllerType.DualShock4USB;

            // Open with shared access
            _cachedDeviceHandle = CreateFile(
                path,
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (_cachedDeviceHandle.IsInvalid)
            {
                _cachedDeviceHandle = null;
                return;
            }

            // Get report length
            IntPtr preparsedData;
            if (HidD_GetPreparsedData(_cachedDeviceHandle, out preparsedData))
            {
                HIDP_CAPS caps;
                if (HidP_GetCaps(preparsedData, out caps) == 0) // HIDP_STATUS_SUCCESS
                {
                    _cachedReportLength = caps.InputReportByteLength;
                }
                HidD_FreePreparsedData(preparsedData);
            }

            // Default report lengths if caps failed
            if (_cachedReportLength == 0)
            {
                switch (_cachedControllerType)
                {
                    case ControllerType.DualSenseUSB:
                        _cachedReportLength = 64;
                        break;
                    case ControllerType.DualSenseBluetooth:
                        _cachedReportLength = 78;
                        break;
                    case ControllerType.DualShock4USB:
                        _cachedReportLength = 64;
                        break;
                    case ControllerType.DualShock4Bluetooth:
                        _cachedReportLength = 78;
                        break;
                    default:
                        _cachedReportLength = 78;
                        break;
                }
            }

            _cachedDevicePath = path;
        }

        private static void CloseController()
        {
            if (_cachedDeviceHandle != null && !_cachedDeviceHandle.IsClosed)
            {
                _cachedDeviceHandle.Close();
            }
            _cachedDeviceHandle = null;
            _cachedDevicePath = null;
            _cachedReportLength = 0;
        }

        private static HidControllerReading ParseHidReport(byte[] report, ControllerType type)
        {
            var result = new HidControllerReading { IsValid = false };

            try
            {
                int offset = 0;

                // DualSense and DualShock 4 have different report formats for USB vs Bluetooth
                switch (type)
                {
                    case ControllerType.DualSenseUSB:
                        // USB report: byte 0 = report ID (0x01), data starts at byte 1
                        offset = 1;
                        return ParseDualSenseReport(report, offset);

                    case ControllerType.DualSenseBluetooth:
                        // Bluetooth report: byte 0 = report ID (0x31), byte 1 = sequence, data starts at byte 2
                        offset = 2;
                        return ParseDualSenseReport(report, offset);

                    case ControllerType.DualShock4USB:
                        // USB report: byte 0 = report ID (0x01), data starts at byte 1
                        offset = 1;
                        return ParseDualShock4Report(report, offset);

                    case ControllerType.DualShock4Bluetooth:
                        // Bluetooth report: byte 0 = report ID (0x11), data starts at byte 2
                        offset = 2;
                        return ParseDualShock4Report(report, offset);

                    default:
                        return result;
                }
            }
            catch
            {
                return result;
            }
        }

        private static HidControllerReading ParseDualSenseReport(byte[] report, int offset)
        {
            var result = new HidControllerReading { IsValid = false };

            if (report.Length < offset + 10)
                return result;

            // DualSense USB report format (report ID 0x01, offset=1):
            // Byte 0: Left stick X (0-255)
            // Byte 1: Left stick Y (0-255)
            // Byte 2: Right stick X (0-255)
            // Byte 3: Right stick Y (0-255)
            // Byte 4: L2 trigger (0-255)
            // Byte 5: R2 trigger (0-255)
            // Byte 6: Counter (ignore)
            // Byte 7: D-pad + face buttons
            // Byte 8: More buttons (L1, R1, etc.)
            // Byte 9: PS + Touchpad buttons

            // DualSense Bluetooth report format (report ID 0x31, offset=2):
            // The Bluetooth report has the SAME layout as USB after the offset
            // Byte 0: Left stick X
            // Byte 1: Left stick Y
            // Byte 2: Right stick X
            // Byte 3: Right stick Y
            // Byte 4: L2 trigger
            // Byte 5: R2 trigger
            // Byte 6: Counter
            // Byte 7: D-pad + face buttons
            // Byte 8: More buttons
            // Byte 9: PS + Touchpad

            result.LeftStickX = report[offset + 0];
            result.LeftStickY = report[offset + 1];
            result.RightStickX = report[offset + 2];
            result.RightStickY = report[offset + 3];

            // Buttons are at fixed positions after thumbsticks and triggers
            byte buttons1 = report[offset + 7];
            byte buttons2 = report[offset + 8];
            byte buttons3 = report[offset + 9];

            // D-pad (low nibble of buttons1): 0=N, 1=NE, 2=E, 3=SE, 4=S, 5=SW, 6=W, 7=NW, 8=released
            int dpad = buttons1 & 0x0F;
            result.DPadUp = (dpad == 0 || dpad == 1 || dpad == 7);
            result.DPadRight = (dpad == 1 || dpad == 2 || dpad == 3);
            result.DPadDown = (dpad == 3 || dpad == 4 || dpad == 5);
            result.DPadLeft = (dpad == 5 || dpad == 6 || dpad == 7);

            // Face buttons (high nibble of buttons1)
            result.Square = (buttons1 & 0x10) != 0;
            result.Cross = (buttons1 & 0x20) != 0;
            result.Circle = (buttons1 & 0x40) != 0;
            result.Triangle = (buttons1 & 0x80) != 0;

            // Shoulder and other buttons (buttons2)
            result.L1 = (buttons2 & 0x01) != 0;
            result.R1 = (buttons2 & 0x02) != 0;
            result.L2 = (buttons2 & 0x04) != 0;
            result.R2 = (buttons2 & 0x08) != 0;
            result.Share = (buttons2 & 0x10) != 0;
            result.Options = (buttons2 & 0x20) != 0;
            result.L3 = (buttons2 & 0x40) != 0;
            result.R3 = (buttons2 & 0x80) != 0;

            // PS and Touchpad (buttons3)
            result.PS = (buttons3 & 0x01) != 0;
            result.Touchpad = (buttons3 & 0x02) != 0;

            result.IsValid = true;
            return result;
        }

        private static HidControllerReading ParseDualShock4Report(byte[] report, int offset)
        {
            var result = new HidControllerReading { IsValid = false };

            if (report.Length < offset + 7)
                return result;

            // DualShock 4 report format (similar to DualSense but slightly different):
            // Byte 0: Left stick X
            // Byte 1: Left stick Y
            // Byte 2: Right stick X
            // Byte 3: Right stick Y
            // Byte 4: D-pad and face buttons (same format as DualSense byte 7)
            // Byte 5: More buttons (same format as DualSense byte 8)
            // Byte 6: PS button and touchpad (bits 0 and 1)

            result.LeftStickX = report[offset + 0];
            result.LeftStickY = report[offset + 1];
            result.RightStickX = report[offset + 2];
            result.RightStickY = report[offset + 3];

            byte buttons1 = report[offset + 4];
            byte buttons2 = report[offset + 5];
            byte buttons3 = report[offset + 6];

            // D-pad
            int dpad = buttons1 & 0x0F;
            result.DPadUp = (dpad == 0 || dpad == 1 || dpad == 7);
            result.DPadRight = (dpad == 1 || dpad == 2 || dpad == 3);
            result.DPadDown = (dpad == 3 || dpad == 4 || dpad == 5);
            result.DPadLeft = (dpad == 5 || dpad == 6 || dpad == 7);

            // Face buttons
            result.Square = (buttons1 & 0x10) != 0;
            result.Cross = (buttons1 & 0x20) != 0;
            result.Circle = (buttons1 & 0x40) != 0;
            result.Triangle = (buttons1 & 0x80) != 0;

            // Shoulder and other buttons
            result.L1 = (buttons2 & 0x01) != 0;
            result.R1 = (buttons2 & 0x02) != 0;
            result.L2 = (buttons2 & 0x04) != 0;
            result.R2 = (buttons2 & 0x08) != 0;
            result.Share = (buttons2 & 0x10) != 0;
            result.Options = (buttons2 & 0x20) != 0;
            result.L3 = (buttons2 & 0x40) != 0;
            result.R3 = (buttons2 & 0x80) != 0;

            // PS and Touchpad
            result.PS = (buttons3 & 0x01) != 0;
            result.Touchpad = (buttons3 & 0x02) != 0;

            result.IsValid = true;
            return result;
        }

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
                // If we found any game controllers, return true
                // The filtering is already done in IsGameControllerDevicePath
                return controllers.Count > 0;
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

        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        // Track enumeration count for diagnostics
        private static int _enumerationCount = 0;

        public static List<string> GetConnectedControllers()
        {
            var controllers = new List<string>();
            IntPtr deviceInfoSet = IntPtr.Zero;
            _enumerationCount++;
            int thisEnumeration = _enumerationCount;

            Logger?.Debug($"[DirectInput] GetConnectedControllers START (enumeration #{thisEnumeration})");

            try
            {
                deviceInfoSet = SetupDiGetClassDevs(ref GUID_DEVINTERFACE_HID, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);

                if (deviceInfoSet == IntPtr.Zero)
                {
                    Logger?.Warn($"[DirectInput] SetupDiGetClassDevs returned IntPtr.Zero (enumeration #{thisEnumeration})");
                    return controllers;
                }
                if (deviceInfoSet == INVALID_HANDLE_VALUE)
                {
                    Logger?.Warn($"[DirectInput] SetupDiGetClassDevs returned INVALID_HANDLE_VALUE (enumeration #{thisEnumeration})");
                    return controllers;
                }

                Logger?.Debug($"[DirectInput] SetupDiGetClassDevs returned handle 0x{deviceInfoSet.ToInt64():X} (enumeration #{thisEnumeration})");

                SP_DEVICE_INTERFACE_DATA deviceInterfaceData = new SP_DEVICE_INTERFACE_DATA();
                deviceInterfaceData.cbSize = (uint)Marshal.SizeOf(deviceInterfaceData);

                uint memberIndex = 0;
                int devicesEnumerated = 0;

                while (SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref GUID_DEVINTERFACE_HID, memberIndex, ref deviceInterfaceData))
                {
                    devicesEnumerated++;
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

                Logger?.Debug($"[DirectInput] Enumerated {devicesEnumerated} HID devices, found {controllers.Count} controllers (enumeration #{thisEnumeration})");
            }
            catch (Exception ex)
            {
                Logger?.Error($"[DirectInput] Enumeration failed (enumeration #{thisEnumeration}): {ex.Message}");
            }
            finally
            {
                if (deviceInfoSet != IntPtr.Zero && deviceInfoSet != INVALID_HANDLE_VALUE)
                {
                    bool destroyed = SetupDiDestroyDeviceInfoList(deviceInfoSet);
                    Logger?.Debug($"[DirectInput] SetupDiDestroyDeviceInfoList returned {destroyed} for handle 0x{deviceInfoSet.ToInt64():X} (enumeration #{thisEnumeration})");
                }
            }

            Logger?.Debug($"[DirectInput] GetConnectedControllers END (enumeration #{thisEnumeration})");
            return controllers;
        }

        /// <summary>Get all HID device paths for diagnostics (unfiltered).</summary>
        public static List<string> GetAllHidDevicePaths()
        {
            var devices = new List<string>();
            IntPtr deviceInfoSet = IntPtr.Zero;

            try
            {
                deviceInfoSet = SetupDiGetClassDevs(ref GUID_DEVINTERFACE_HID, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
                if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == INVALID_HANDLE_VALUE) return devices;

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
                                devices.Add(detailData.DevicePath);
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
                if (deviceInfoSet != IntPtr.Zero && deviceInfoSet != INVALID_HANDLE_VALUE)
                {
                    SetupDiDestroyDeviceInfoList(deviceInfoSet);
                }
            }

            return devices;
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

            // PlayStation controllers (USB format: vid_054c&pid_xxxx)
            // PS4 DualShock 4: 05c4 (v1), 09cc (v2)
            // PS5 DualSense: 0ce6, 0df2 (Edge)
            bool isPlayStationUsb = lowerPath.Contains("vid_054c") && (
                lowerPath.Contains("pid_05c4") || lowerPath.Contains("pid_09cc") ||  // DS4
                lowerPath.Contains("pid_0ce6") || lowerPath.Contains("pid_0df2") ||  // DualSense
                lowerPath.Contains("pid_0ba0"));

            // PlayStation controllers (Bluetooth format: _vid&0002054c_pid&0ce6)
            // Bluetooth HID uses format like: {00001124-...}_vid&0002054c_pid&0ce6
            bool isPlayStationBluetooth = lowerPath.Contains("_vid&0002054c") && (
                lowerPath.Contains("_pid&05c4") || lowerPath.Contains("_pid&09cc") ||  // DS4
                lowerPath.Contains("_pid&0ce6") || lowerPath.Contains("_pid&0df2") ||  // DualSense
                lowerPath.Contains("_pid&0ba0"));

            bool isPlayStationController = isPlayStationUsb || isPlayStationBluetooth;

            // Bluetooth patterns (general)
            bool isBluetoothController =
                lowerPath.Contains("xbox") || lowerPath.Contains("wireless") ||
                (lowerPath.Contains("045e") && lowerPath.Contains("vid:{")) ||
                lowerPath.Contains("00001812-0000-1000-8000-00805f9b34fb") ||
                lowerPath.Contains("00001124-0000-1000-8000-00805f9b34fb") ||  // Bluetooth HID profile
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

                // Bluetooth HID format: {00001124-...}_vid&0002054c_pid&0ce6
                if (lowerPath.Contains("_vid&0002054c"))
                {
                    if (lowerPath.Contains("_pid&0ce6"))
                        return "DualSense Controller (Bluetooth)";
                    if (lowerPath.Contains("_pid&0df2"))
                        return "DualSense Edge (Bluetooth)";
                    if (lowerPath.Contains("_pid&05c4") || lowerPath.Contains("_pid&09cc"))
                        return "DualShock 4 (Bluetooth)";
                    return "PlayStation Controller (Bluetooth)";
                }

                // Bluetooth HID format for Xbox: _vid&0002045e
                if (lowerPath.Contains("_vid&0002045e"))
                {
                    return "Xbox Wireless Controller (Bluetooth)";
                }

                // Old Bluetooth format with vid:{...}
                if (lowerPath.Contains("vid:{") && lowerPath.Contains("pid:"))
                {
                    if (lowerPath.Contains("xbox") || lowerPath.Contains("wireless") || lowerPath.Contains("045e"))
                        return "Xbox Wireless Controller (Bluetooth)";
                    if (lowerPath.Contains("054c"))
                    {
                        if (lowerPath.Contains("0ce6") || lowerPath.Contains("dualsense"))
                            return "DualSense Controller (Bluetooth)";
                        if (lowerPath.Contains("05c4") || lowerPath.Contains("09cc") || lowerPath.Contains("dualshock"))
                            return "DualShock 4 (Bluetooth)";
                        return "PlayStation Controller (Bluetooth)";
                    }

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
                            {
                                if (pid == "PID_0CE6")
                                    return "DualSense Controller (USB)";
                                if (pid == "PID_0DF2")
                                    return "DualSense Edge (USB)";
                                if (pid == "PID_05C4" || pid == "PID_09CC")
                                    return "DualShock 4 (USB)";
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
                // Parsing failed
            }

            return "Unknown HID Game Controller";
        }
    }
}
