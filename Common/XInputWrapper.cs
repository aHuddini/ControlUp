using System;
using System.Runtime.InteropServices;

namespace ControlUp.Common
{
    /// <summary>
    /// Wrapper for XInput API to detect Xbox controllers
    /// </summary>
    public static class XInputWrapper
    {
        // XInput API imports
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

        // XInput constants
        private const uint XINPUT_DEVTYPE_GAMEPAD = 0x01;
        private const uint XINPUT_DEVSUBTYPE_GAMEPAD = 0x01;
        private const uint ERROR_SUCCESS = 0;
        private const uint ERROR_DEVICE_NOT_CONNECTED = 1167;

        // XInput button constants (public for dialog use)
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

        /// <summary>
        /// Public wrapper for XInputGetState for dialog use
        /// </summary>
        public static uint GetState(uint dwUserIndex, ref XINPUT_STATE pState)
        {
            return XInputGetState(dwUserIndex, ref pState);
        }

        /// <summary>
        /// Checks if an Xbox controller is currently connected
        /// </summary>
        /// <returns>True if an Xbox controller is detected, false otherwise</returns>
        public static bool IsControllerConnected()
        {
            // Check all possible controller slots (0-3)
            for (uint i = 0; i < 4; i++)
            {
                XINPUT_STATE state = new XINPUT_STATE();
                uint result = XInputGetState(i, ref state);

                if (result == ERROR_SUCCESS)
                {
                    // Also check if it's an Xbox controller
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

        /// <summary>
        /// Checks if an Xbox controller is connected to a specific slot
        /// </summary>
        /// <param name="slot">Controller slot (0-3)</param>
        /// <returns>True if a controller is connected to the specified slot</returns>
        public static bool IsControllerConnectedToSlot(uint slot)
        {
            if (slot >= 4) return false;

            XINPUT_STATE state = new XINPUT_STATE();
            uint result = XInputGetState(slot, ref state);

            if (result == ERROR_SUCCESS)
            {
                // Also check if it's an Xbox controller
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