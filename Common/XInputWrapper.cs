using System.Runtime.InteropServices;

namespace ControlUp.Common
{
    /// <summary>Minimal XInput API wrapper for controller connection detection only.</summary>
    public static class XInputWrapper
    {
        [DllImport("xinput1_4.dll")]
        private static extern uint XInputGetState(uint dwUserIndex, ref XINPUT_STATE pState);

        [DllImport("xinput1_4.dll")]
        private static extern uint XInputGetCapabilities(uint dwUserIndex, uint dwFlags, ref XINPUT_CAPABILITIES pCapabilities);

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
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
        private struct XINPUT_CAPABILITIES
        {
            public byte Type;
            public byte SubType;
            public ushort Flags;
            public XINPUT_GAMEPAD Gamepad;
            public XINPUT_VIBRATION Vibration;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_VIBRATION
        {
            public ushort wLeftMotorSpeed;
            public ushort wRightMotorSpeed;
        }

        private const uint XINPUT_DEVTYPE_GAMEPAD = 0x01;
        private const uint XINPUT_DEVSUBTYPE_GAMEPAD = 0x01;
        private const uint ERROR_SUCCESS = 0;

        /// <summary>Check if any XInput controller is connected (slots 0-3).</summary>
        public static bool IsControllerConnected()
        {
            for (uint i = 0; i < 4; i++)
            {
                if (IsControllerConnectedToSlot(i))
                    return true;
            }
            return false;
        }

        /// <summary>Check if a controller is connected to a specific slot (0-3).</summary>
        public static bool IsControllerConnectedToSlot(uint slot)
        {
            if (slot >= 4) return false;

            try
            {
                XINPUT_STATE state = new XINPUT_STATE();
                if (XInputGetState(slot, ref state) == ERROR_SUCCESS)
                {
                    XINPUT_CAPABILITIES capabilities = new XINPUT_CAPABILITIES();
                    uint capResult = XInputGetCapabilities(slot, 0, ref capabilities);

                    return capResult == ERROR_SUCCESS &&
                           capabilities.Type == XINPUT_DEVTYPE_GAMEPAD &&
                           capabilities.SubType == XINPUT_DEVSUBTYPE_GAMEPAD;
                }
            }
            catch { }

            return false;
        }
    }
}
