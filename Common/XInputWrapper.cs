using System.Runtime.InteropServices;

namespace ControlUp.Common
{
    /// <summary>XInput API wrapper for Xbox controller detection.</summary>
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
        private const uint XINPUT_DEVTYPE_GAMEPAD = 0x01;
        private const uint XINPUT_DEVSUBTYPE_GAMEPAD = 0x01;
        public const uint ERROR_SUCCESS = 0;
        public const uint ERROR_EMPTY = 0x10D2;
        private const uint ERROR_DEVICE_NOT_CONNECTED = 1167;
        public const uint XUSER_INDEX_ANY = 0x000000FF;

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

        public static bool IsControllerConnected()
        {
            for (uint i = 0; i < 4; i++)
            {
                XINPUT_STATE state = new XINPUT_STATE();
                if (XInputGetState(i, ref state) == ERROR_SUCCESS)
                {
                    XINPUT_CAPABILITIES capabilities = new XINPUT_CAPABILITIES();
                    uint capResult = XInputGetCapabilities(i, 0, ref capabilities);

                    if (capResult == ERROR_SUCCESS &&
                        capabilities.Type == XINPUT_DEVTYPE_GAMEPAD &&
                        capabilities.SubType == XINPUT_DEVSUBTYPE_GAMEPAD)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static bool IsControllerConnectedToSlot(uint slot)
        {
            if (slot >= 4) return false;

            XINPUT_STATE state = new XINPUT_STATE();
            if (XInputGetState(slot, ref state) == ERROR_SUCCESS)
            {
                XINPUT_CAPABILITIES capabilities = new XINPUT_CAPABILITIES();
                uint capResult = XInputGetCapabilities(slot, 0, ref capabilities);

                if (capResult == ERROR_SUCCESS &&
                    capabilities.Type == XINPUT_DEVTYPE_GAMEPAD &&
                    capabilities.SubType == XINPUT_DEVSUBTYPE_GAMEPAD)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
