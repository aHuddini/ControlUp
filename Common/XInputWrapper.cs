using System.Runtime.InteropServices;

namespace ControlUp.Common
{
    /// <summary>XInput API wrapper for controller detection and input.</summary>
    public static class XInputWrapper
    {
        [DllImport("xinput1_4.dll")]
        private static extern uint XInputGetState(uint dwUserIndex, ref XINPUT_STATE pState);

        [DllImport("xinput1_4.dll")]
        private static extern uint XInputGetCapabilities(uint dwUserIndex, uint dwFlags, ref XINPUT_CAPABILITIES pCapabilities);

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_CAPABILITIES
        {
            public byte Type;
            public byte SubType;
            public ushort Flags;
            public XINPUT_GAMEPAD Gamepad;
            public XINPUT_VIBRATION Vibration;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_VIBRATION
        {
            public ushort wLeftMotorSpeed;
            public ushort wRightMotorSpeed;
        }

        // Constants
        private const byte XINPUT_DEVTYPE_GAMEPAD = 0x01;
        public const uint ERROR_SUCCESS = 0;
        public const uint ERROR_DEVICE_NOT_CONNECTED = 1167;

        // SubType values for identifying controller type
        private const byte XINPUT_DEVSUBTYPE_UNKNOWN = 0x00;
        private const byte XINPUT_DEVSUBTYPE_GAMEPAD = 0x01;
        private const byte XINPUT_DEVSUBTYPE_WHEEL = 0x02;
        private const byte XINPUT_DEVSUBTYPE_ARCADE_STICK = 0x03;
        private const byte XINPUT_DEVSUBTYPE_FLIGHT_STICK = 0x04;
        private const byte XINPUT_DEVSUBTYPE_DANCE_PAD = 0x05;
        private const byte XINPUT_DEVSUBTYPE_GUITAR = 0x06;
        private const byte XINPUT_DEVSUBTYPE_DRUM_KIT = 0x08;

        // Capability flags
        private const ushort XINPUT_CAPS_WIRELESS = 0x0002;

        // Button masks
        public const ushort XINPUT_GAMEPAD_DPAD_UP = 0x0001;
        public const ushort XINPUT_GAMEPAD_DPAD_DOWN = 0x0002;
        public const ushort XINPUT_GAMEPAD_DPAD_LEFT = 0x0004;
        public const ushort XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008;
        public const ushort XINPUT_GAMEPAD_START = 0x0010;
        public const ushort XINPUT_GAMEPAD_BACK = 0x0020;
        public const ushort XINPUT_GAMEPAD_LEFT_THUMB = 0x0040;
        public const ushort XINPUT_GAMEPAD_RIGHT_THUMB = 0x0080;
        public const ushort XINPUT_GAMEPAD_LEFT_SHOULDER = 0x0100;
        public const ushort XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200;
        public const ushort XINPUT_GAMEPAD_A = 0x1000;
        public const ushort XINPUT_GAMEPAD_B = 0x2000;
        public const ushort XINPUT_GAMEPAD_X = 0x4000;
        public const ushort XINPUT_GAMEPAD_Y = 0x8000;

        public static uint GetState(uint dwUserIndex, ref XINPUT_STATE pState)
        {
            return XInputGetState(dwUserIndex, ref pState);
        }

        /// <summary>Check if any XInput controller is connected.</summary>
        public static bool IsControllerConnected()
        {
            for (uint i = 0; i < 4; i++)
            {
                XINPUT_STATE state = new XINPUT_STATE();
                if (XInputGetState(i, ref state) == ERROR_SUCCESS)
                    return true;
            }
            return false;
        }

        /// <summary>Get the first connected controller's slot (0-3), or -1 if none.</summary>
        public static int GetFirstConnectedSlot()
        {
            for (uint i = 0; i < 4; i++)
            {
                XINPUT_STATE state = new XINPUT_STATE();
                if (XInputGetState(i, ref state) == ERROR_SUCCESS)
                    return (int)i;
            }
            return -1;
        }

        /// <summary>Get a friendly name for the connected controller.</summary>
        public static string GetControllerName()
        {
            for (uint i = 0; i < 4; i++)
            {
                XINPUT_STATE state = new XINPUT_STATE();
                if (XInputGetState(i, ref state) == ERROR_SUCCESS)
                {
                    XINPUT_CAPABILITIES caps = new XINPUT_CAPABILITIES();
                    if (XInputGetCapabilities(i, 0, ref caps) == ERROR_SUCCESS)
                    {
                        return GetControllerNameFromCapabilities(caps);
                    }
                    return "XInput Controller";
                }
            }
            return null;
        }

        public class ControllerInfo
        {
            public bool Connected { get; set; }
            public string Name { get; set; }
            public bool IsWireless { get; set; }
        }

        /// <summary>Get controller info including name and whether it's wireless.</summary>
        public static ControllerInfo GetControllerInfo()
        {
            for (uint i = 0; i < 4; i++)
            {
                XINPUT_STATE state = new XINPUT_STATE();
                if (XInputGetState(i, ref state) == ERROR_SUCCESS)
                {
                    XINPUT_CAPABILITIES caps = new XINPUT_CAPABILITIES();
                    if (XInputGetCapabilities(i, 0, ref caps) == ERROR_SUCCESS)
                    {
                        bool isWireless = (caps.Flags & XINPUT_CAPS_WIRELESS) != 0;
                        string name = GetControllerNameFromCapabilities(caps);
                        return new ControllerInfo { Connected = true, Name = name, IsWireless = isWireless };
                    }
                    return new ControllerInfo { Connected = true, Name = "XInput Controller", IsWireless = false };
                }
            }
            return new ControllerInfo { Connected = false, Name = null, IsWireless = false };
        }

        private static string GetControllerNameFromCapabilities(XINPUT_CAPABILITIES caps)
        {
            if (caps.Type != XINPUT_DEVTYPE_GAMEPAD)
                return "XInput Device";

            bool isWireless = (caps.Flags & XINPUT_CAPS_WIRELESS) != 0;
            string connection = isWireless ? "Wireless" : "";

            switch (caps.SubType)
            {
                case XINPUT_DEVSUBTYPE_GAMEPAD:
                    // Most Xbox controllers report as standard gamepad
                    return isWireless ? "Xbox Wireless Controller" : "Xbox Controller";
                case XINPUT_DEVSUBTYPE_WHEEL:
                    return "Racing Wheel";
                case XINPUT_DEVSUBTYPE_ARCADE_STICK:
                    return "Arcade Stick";
                case XINPUT_DEVSUBTYPE_FLIGHT_STICK:
                    return "Flight Stick";
                case XINPUT_DEVSUBTYPE_DANCE_PAD:
                    return "Dance Pad";
                case XINPUT_DEVSUBTYPE_GUITAR:
                    return "Guitar Controller";
                case XINPUT_DEVSUBTYPE_DRUM_KIT:
                    return "Drum Kit";
                default:
                    return isWireless ? "Wireless Controller" : "XInput Controller";
            }
        }
    }
}
