using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ControlUp.Common
{
    /// <summary>Detects game controllers via HID (Xbox, PlayStation, Switch, etc.).</summary>
    public static class HidControllerDetector
    {
        // SetupAPI imports
        [DllImport("hid.dll", SetLastError = true)]
        private static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetAttributes(IntPtr hidDeviceObject, ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool HidD_GetProductString(IntPtr hidDeviceObject, StringBuilder buffer, uint bufferLength);

        [DllImport("hid.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool HidD_GetManufacturerString(IntPtr hidDeviceObject, StringBuilder buffer, uint bufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(IntPtr hidDeviceObject, out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern uint HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

        private const uint HIDP_STATUS_SUCCESS = 0x00110000;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

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

        private const uint DIGCF_PRESENT = 0x02;
        private const uint DIGCF_DEVICEINTERFACE = 0x10;
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x01;
        private const uint FILE_SHARE_WRITE = 0x02;
        private const uint OPEN_EXISTING = 3;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        // HID Usage Pages for game controllers
        private const ushort HID_USAGE_PAGE_GENERIC = 0x01;
        private const ushort HID_USAGE_GENERIC_JOYSTICK = 0x04;
        private const ushort HID_USAGE_GENERIC_GAMEPAD = 0x05;
        private const ushort HID_USAGE_GENERIC_MULTI_AXIS = 0x08;

        // Known vendor IDs
        private static readonly Dictionary<ushort, string> KnownVendors = new Dictionary<ushort, string>
        {
            { 0x045E, "Microsoft" },      // Xbox controllers
            { 0x054C, "Sony" },           // PlayStation controllers
            { 0x057E, "Nintendo" },       // Switch controllers
            { 0x0738, "Mad Catz" },
            { 0x0E6F, "PDP" },
            { 0x1532, "Razer" },
            { 0x24C6, "PowerA" },
            { 0x2DC8, "8BitDo" },
            { 0x046D, "Logitech" },
            { 0x28DE, "Valve" },          // Steam Controller
            { 0x2563, "ShanWan" },
            { 0x20D6, "PowerA/BDA" },
        };

        public enum ConnectionType
        {
            Unknown,
            USB,
            Bluetooth,
            Wireless  // For proprietary wireless (e.g., Xbox wireless adapter)
        }

        public class ControllerInfo
        {
            public string Name { get; set; }
            public string Manufacturer { get; set; }
            public ushort VendorId { get; set; }
            public ushort ProductId { get; set; }
            public string Type { get; set; }
            public ConnectionType Connection { get; set; }
            public string DevicePath { get; set; }

            /// <summary>Returns a display string like "8BitDo Pro 2 (Bluetooth)" or "Xbox Controller (USB)"</summary>
            public string DisplayName => Connection != ConnectionType.Unknown
                ? $"{Name} ({Connection})"
                : Name;
        }

        /// <summary>Returns all connected HID game controllers.</summary>
        public static List<ControllerInfo> GetConnectedControllers()
        {
            var controllers = new List<ControllerInfo>();

            try
            {
                HidD_GetHidGuid(out Guid hidGuid);
                IntPtr deviceInfoSet = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);

                if (deviceInfoSet == INVALID_HANDLE_VALUE)
                    return controllers;

                try
                {
                    SP_DEVICE_INTERFACE_DATA deviceInterfaceData = new SP_DEVICE_INTERFACE_DATA();
                    deviceInterfaceData.cbSize = Marshal.SizeOf(deviceInterfaceData);

                    uint index = 0;
                    while (SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref hidGuid, index, ref deviceInterfaceData))
                    {
                        index++;

                        // Get device path
                        SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref deviceInterfaceData, IntPtr.Zero, 0, out uint requiredSize, IntPtr.Zero);

                        IntPtr detailDataBuffer = Marshal.AllocHGlobal((int)requiredSize);
                        try
                        {
                            // Set cbSize for the structure (different on x86 vs x64)
                            Marshal.WriteInt32(detailDataBuffer, IntPtr.Size == 8 ? 8 : 6);

                            if (SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref deviceInterfaceData, detailDataBuffer, requiredSize, out _, IntPtr.Zero))
                            {
                                string devicePath = Marshal.PtrToStringAuto(detailDataBuffer + 4);
                                var controller = GetControllerInfo(devicePath);
                                if (controller != null)
                                {
                                    controllers.Add(controller);
                                }
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(detailDataBuffer);
                        }
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(deviceInfoSet);
                }
            }
            catch
            {
                // Enumeration failed, return empty list
            }

            return controllers;
        }

        private static ControllerInfo GetControllerInfo(string devicePath)
        {
            // Try multiple access modes - some controllers don't allow write access
            IntPtr handle = CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

            if (handle == INVALID_HANDLE_VALUE)
            {
                handle = CreateFile(devicePath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            }

            if (handle == INVALID_HANDLE_VALUE)
            {
                // Try with no access flags - just for querying
                handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            }

            if (handle == INVALID_HANDLE_VALUE)
                return null;

            try
            {
                // Check if this is a game controller
                if (!HidD_GetPreparsedData(handle, out IntPtr preparsedData))
                    return null;

                try
                {
                    if (HidP_GetCaps(preparsedData, out HIDP_CAPS caps) != HIDP_STATUS_SUCCESS)
                        return null;

                    // Check if it's a gamepad or joystick
                    if (caps.UsagePage != HID_USAGE_PAGE_GENERIC)
                        return null;

                    if (caps.Usage != HID_USAGE_GENERIC_GAMEPAD &&
                        caps.Usage != HID_USAGE_GENERIC_JOYSTICK &&
                        caps.Usage != HID_USAGE_GENERIC_MULTI_AXIS)
                        return null;

                    // Get device attributes
                    HIDD_ATTRIBUTES attributes = new HIDD_ATTRIBUTES();
                    attributes.Size = Marshal.SizeOf(attributes);

                    if (!HidD_GetAttributes(handle, ref attributes))
                        return null;

                    // Get product name
                    StringBuilder productName = new StringBuilder(256);
                    if (!HidD_GetProductString(handle, productName, 256))
                        productName.Clear();

                    // Get manufacturer name
                    StringBuilder manufacturerName = new StringBuilder(256);
                    if (!HidD_GetManufacturerString(handle, manufacturerName, 256))
                        manufacturerName.Clear();

                    string name = productName.Length > 0 ? productName.ToString().TrimEnd('\0') : "Unknown Controller";
                    string manufacturer = manufacturerName.Length > 0 ? manufacturerName.ToString().TrimEnd('\0') : GetVendorName(attributes.VendorID);

                    return new ControllerInfo
                    {
                        Name = name,
                        Manufacturer = manufacturer,
                        VendorId = attributes.VendorID,
                        ProductId = attributes.ProductID,
                        Type = GetControllerType(attributes.VendorID, caps.Usage),
                        Connection = GetConnectionType(devicePath),
                        DevicePath = devicePath
                    };
                }
                finally
                {
                    HidD_FreePreparsedData(preparsedData);
                }
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static string GetVendorName(ushort vendorId)
        {
            return KnownVendors.TryGetValue(vendorId, out string name) ? name : $"VID_{vendorId:X4}";
        }

        private static string GetControllerType(ushort vendorId, ushort usage)
        {
            string baseType = usage == HID_USAGE_GENERIC_GAMEPAD ? "Gamepad" : "Joystick";

            switch (vendorId)
            {
                case 0x045E: return "Xbox Controller";
                case 0x054C: return "PlayStation Controller";
                case 0x057E: return "Nintendo Controller";
                case 0x28DE: return "Steam Controller";
                default: return baseType;
            }
        }

        private static ConnectionType GetConnectionType(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath))
                return ConnectionType.Unknown;

            string pathUpper = devicePath.ToUpperInvariant();

            // Bluetooth indicators in device path
            if (pathUpper.Contains("BTHENUM") ||
                pathUpper.Contains("BLUETOOTHLE") ||
                pathUpper.Contains("BTH") ||
                pathUpper.Contains("{00001124-0000-1000-8000-00805F9B34FB}"))  // Bluetooth HID GUID
            {
                return ConnectionType.Bluetooth;
            }

            // Xbox Wireless Adapter (proprietary 2.4GHz, not Bluetooth)
            // VID 045E (Microsoft) with specific wireless adapter PIDs
            if (pathUpper.Contains("VID_045E") &&
                (pathUpper.Contains("PID_02E0") ||  // Xbox Wireless Adapter
                 pathUpper.Contains("PID_02FE") ||  // Xbox Wireless Adapter v2
                 pathUpper.Contains("PID_0719")))   // Xbox 360 Wireless Receiver
            {
                return ConnectionType.Wireless;
            }

            // USB indicators
            if (pathUpper.Contains("USB#") ||
                pathUpper.Contains("\\USB\\") ||
                pathUpper.Contains("VID_"))  // VID_ without Bluetooth markers = USB
            {
                return ConnectionType.USB;
            }

            return ConnectionType.Unknown;
        }

        /// <summary>Returns true if any game controller is connected.</summary>
        public static bool IsAnyControllerConnected()
        {
            return GetConnectedControllers().Count > 0;
        }
    }
}
