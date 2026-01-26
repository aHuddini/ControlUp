using System.Runtime.InteropServices;

namespace ControlUp.Common
{
    /// <summary>Minimal XInput wrapper for controller connection detection only.
    /// Button input is handled by Playnite SDK's OnDesktopControllerButtonStateChanged.</summary>
    public static class XInputWrapper
    {
        [DllImport("xinput1_4.dll")]
        private static extern uint XInputGetState(uint dwUserIndex, ref XINPUT_STATE pState);

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

        private const uint ERROR_SUCCESS = 0;

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
    }
}
