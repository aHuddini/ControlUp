using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ControlUp.Common
{
    /// <summary>Windows.Gaming.Input API wrapper - primary detection for modern controllers (DualSense, etc.).</summary>
    public static class GamingInputWrapper
    {
        private static bool _initialized = false;
        private static bool _isAvailable = false;
        private static Type _gamepadType;
        private static Type _rawGameControllerType;

        // WinRT activation
        [DllImport("combase.dll", PreserveSig = false)]
        private static extern void RoInitialize(RO_INIT_TYPE initType);

        [DllImport("combase.dll", PreserveSig = false)]
        private static extern void RoGetActivationFactory(
            [MarshalAs(UnmanagedType.HString)] string activatableClassId,
            [In] ref Guid iid,
            [Out, MarshalAs(UnmanagedType.IInspectable)] out object factory);

        private enum RO_INIT_TYPE
        {
            RO_INIT_SINGLETHREADED = 0,
            RO_INIT_MULTITHREADED = 1
        }

        // IGamepadStatics interface GUID
        private static readonly Guid IID_IGamepadStatics = new Guid("8BBCE529-D49C-39E9-9560-E47DDE96B7C8");
        // IRawGameControllerStatics interface GUID
        private static readonly Guid IID_IRawGameControllerStatics = new Guid("EB8D0792-E95A-4B19-AFC7-0A59F8BF759E");

        static GamingInputWrapper()
        {
            Initialize();
        }

        private static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                // Try to initialize WinRT
                try
                {
                    RoInitialize(RO_INIT_TYPE.RO_INIT_MULTITHREADED);
                }
                catch
                {
                    // Already initialized or not available
                }

                // Try to get Gamepad factory
                object gamepadFactory = null;
                try
                {
                    var iid = IID_IGamepadStatics;
                    RoGetActivationFactory("Windows.Gaming.Input.Gamepad", ref iid, out gamepadFactory);
                    if (gamepadFactory != null)
                    {
                        _isAvailable = true;
                    }
                }
                catch
                {
                    // Gamepad not available
                }

                // Also try RawGameController for broader support
                if (!_isAvailable)
                {
                    try
                    {
                        object rawFactory = null;
                        var iid = IID_IRawGameControllerStatics;
                        RoGetActivationFactory("Windows.Gaming.Input.RawGameController", ref iid, out rawFactory);
                        if (rawFactory != null)
                        {
                            _isAvailable = true;
                        }
                    }
                    catch
                    {
                        // RawGameController not available
                    }
                }

                // Alternative: Try loading via Type.GetType with Windows Runtime types
                if (!_isAvailable)
                {
                    try
                    {
                        _gamepadType = Type.GetType("Windows.Gaming.Input.Gamepad, Windows.Gaming.Input, ContentType=WindowsRuntime");
                        _rawGameControllerType = Type.GetType("Windows.Gaming.Input.RawGameController, Windows.Gaming.Input, ContentType=WindowsRuntime");

                        if (_gamepadType != null || _rawGameControllerType != null)
                        {
                            _isAvailable = true;
                        }
                    }
                    catch
                    {
                        // Type loading failed
                    }
                }
            }
            catch
            {
                _isAvailable = false;
            }
        }

        /// <summary>Whether Windows.Gaming.Input API is available on this system.</summary>
        public static bool IsAvailable => _isAvailable;

        // GamepadButtons flags (matches Windows.Gaming.Input.GamepadButtons)
        [Flags]
        public enum GamepadButtons : uint
        {
            None = 0,
            Menu = 0x1,           // Start
            View = 0x2,           // Back/Select
            A = 0x4,
            B = 0x8,
            X = 0x10,
            Y = 0x20,
            DPadUp = 0x40,
            DPadDown = 0x80,
            DPadLeft = 0x100,
            DPadRight = 0x200,
            LeftShoulder = 0x400,
            RightShoulder = 0x800,
            LeftThumbstick = 0x1000,
            RightThumbstick = 0x2000,
            Paddle1 = 0x4000,
            Paddle2 = 0x8000,
            Paddle3 = 0x10000,
            Paddle4 = 0x20000
        }

        /// <summary>Controller input state from Windows.Gaming.Input.</summary>
        public class ControllerReading
        {
            public bool IsValid { get; set; }
            public GamepadButtons Buttons { get; set; }
            public double LeftThumbstickX { get; set; }
            public double LeftThumbstickY { get; set; }
            public double RightThumbstickX { get; set; }
            public double RightThumbstickY { get; set; }
            public double LeftTrigger { get; set; }
            public double RightTrigger { get; set; }
        }

        /// <summary>Get current button/input state from the first connected gamepad.</summary>
        public static ControllerReading GetCurrentReading()
        {
            var result = new ControllerReading { IsValid = false };
            if (!_isAvailable || _gamepadType == null) return result;

            try
            {
                var gamepadsProperty = _gamepadType.GetProperty("Gamepads");
                if (gamepadsProperty == null) return result;

                var gamepads = gamepadsProperty.GetValue(null) as System.Collections.IEnumerable;
                if (gamepads == null) return result;

                foreach (var gamepad in gamepads)
                {
                    // Call GetCurrentReading() on the gamepad
                    var getCurrentReadingMethod = gamepad.GetType().GetMethod("GetCurrentReading");
                    if (getCurrentReadingMethod == null) continue;

                    var reading = getCurrentReadingMethod.Invoke(gamepad, null);
                    if (reading == null) continue;

                    var readingType = reading.GetType();

                    // Get Buttons property (GamepadButtons enum)
                    var buttonsProp = readingType.GetProperty("Buttons");
                    if (buttonsProp != null)
                    {
                        var buttonsValue = buttonsProp.GetValue(reading);
                        result.Buttons = (GamepadButtons)Convert.ToUInt32(buttonsValue);
                    }

                    // Get thumbstick values
                    var leftThumbXProp = readingType.GetProperty("LeftThumbstickX");
                    var leftThumbYProp = readingType.GetProperty("LeftThumbstickY");
                    var rightThumbXProp = readingType.GetProperty("RightThumbstickX");
                    var rightThumbYProp = readingType.GetProperty("RightThumbstickY");

                    if (leftThumbXProp != null)
                        result.LeftThumbstickX = Convert.ToDouble(leftThumbXProp.GetValue(reading));
                    if (leftThumbYProp != null)
                        result.LeftThumbstickY = Convert.ToDouble(leftThumbYProp.GetValue(reading));
                    if (rightThumbXProp != null)
                        result.RightThumbstickX = Convert.ToDouble(rightThumbXProp.GetValue(reading));
                    if (rightThumbYProp != null)
                        result.RightThumbstickY = Convert.ToDouble(rightThumbYProp.GetValue(reading));

                    // Get trigger values
                    var leftTriggerProp = readingType.GetProperty("LeftTrigger");
                    var rightTriggerProp = readingType.GetProperty("RightTrigger");

                    if (leftTriggerProp != null)
                        result.LeftTrigger = Convert.ToDouble(leftTriggerProp.GetValue(reading));
                    if (rightTriggerProp != null)
                        result.RightTrigger = Convert.ToDouble(rightTriggerProp.GetValue(reading));

                    result.IsValid = true;
                    return result;
                }
            }
            catch
            {
                // Reading failed
            }

            return result;
        }

        /// <summary>Check if any controller is detected via Windows.Gaming.Input.</summary>
        public static bool IsControllerConnected()
        {
            if (!_isAvailable) return false;

            try
            {
                return GetControllerCount() > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Get the count of connected controllers.</summary>
        public static int GetControllerCount()
        {
            if (!_isAvailable) return 0;

            try
            {
                int count = 0;

                // Try Gamepad.Gamepads
                if (_gamepadType != null)
                {
                    var gamepadsProperty = _gamepadType.GetProperty("Gamepads");
                    if (gamepadsProperty != null)
                    {
                        var gamepads = gamepadsProperty.GetValue(null) as System.Collections.IEnumerable;
                        if (gamepads != null)
                        {
                            foreach (var _ in gamepads) count++;
                        }
                    }
                }

                // Try RawGameController.RawGameControllers for broader detection
                if (count == 0 && _rawGameControllerType != null)
                {
                    var controllersProperty = _rawGameControllerType.GetProperty("RawGameControllers");
                    if (controllersProperty != null)
                    {
                        var controllers = controllersProperty.GetValue(null) as System.Collections.IEnumerable;
                        if (controllers != null)
                        {
                            foreach (var _ in controllers) count++;
                        }
                    }
                }

                return count;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>Get controller name - falls back to HID enumeration if needed.</summary>
        public static string GetControllerName()
        {
            if (!_isAvailable)
            {
                return GetControllerNameFromHid();
            }

            try
            {
                // Try to get name from RawGameController (has DisplayName property)
                if (_rawGameControllerType != null)
                {
                    var controllersProperty = _rawGameControllerType.GetProperty("RawGameControllers");
                    if (controllersProperty != null)
                    {
                        var controllers = controllersProperty.GetValue(null) as System.Collections.IEnumerable;
                        if (controllers != null)
                        {
                            foreach (var controller in controllers)
                            {
                                var displayNameProp = controller.GetType().GetProperty("DisplayName");
                                if (displayNameProp != null)
                                {
                                    var name = displayNameProp.GetValue(controller) as string;
                                    if (!string.IsNullOrEmpty(name))
                                        return name;
                                }
                            }
                        }
                    }
                }

                // Fall back to HID
                var hidName = GetControllerNameFromHid();
                return hidName ?? "Game Controller";
            }
            catch
            {
                return GetControllerNameFromHid();
            }
        }

        private static string GetControllerNameFromHid()
        {
            try
            {
                var controllers = DirectInputWrapper.GetConnectedControllerNames();
                if (controllers != null && controllers.Count > 0)
                {
                    foreach (var name in controllers)
                    {
                        if (!string.IsNullOrEmpty(name) &&
                            !name.Contains("Error") &&
                            !name.Contains("Unknown"))
                        {
                            return name;
                        }
                    }
                    return controllers[0];
                }
            }
            catch { }

            return null;
        }
    }
}
