namespace ControlUp.Common
{
    /// <summary>
    /// Unified controller detection. Priority order:
    /// 1. Windows.Gaming.Input (latest API, best for modern controllers like DualSense)
    /// 2. XInput (Xbox controllers and XInput-compatible devices)
    /// 3. HID enumeration (fallback for other controllers)
    /// </summary>
    public static class ControllerDetector
    {
        public enum DetectionSource
        {
            None,
            WindowsGamingInput,
            XInput,
            HidEnumeration
        }

        public class ControllerState
        {
            public bool IsConnected { get; set; }
            public string Name { get; set; }
            public bool IsWireless { get; set; }
            public DetectionSource Source { get; set; }
        }

        /// <summary>
        /// Check if any XInput controller is connected.
        /// </summary>
        public static bool IsXInputControllerConnected()
        {
            return XInputWrapper.IsControllerConnected();
        }

        /// <summary>
        /// Check if any controller is connected using all available APIs.
        /// Windows.Gaming.Input first, then XInput, then HID enumeration.
        /// </summary>
        public static bool IsAnyControllerConnected()
        {
            // Windows.Gaming.Input first (latest API, best for modern controllers)
            if (GamingInputWrapper.IsControllerConnected())
                return true;

            // XInput second (Xbox controllers)
            if (XInputWrapper.IsControllerConnected())
                return true;

            // HID enumeration as last resort
            if (DirectInputWrapper.IsControllerConnected())
                return true;

            return false;
        }

        /// <summary>
        /// Get full controller state with name and detection source.
        /// Windows.Gaming.Input is checked first for modern controller support.
        /// </summary>
        public static ControllerState GetControllerState(bool xinputOnly = false)
        {
            if (xinputOnly)
            {
                // XInput-only mode requested
                var xinputInfo = XInputWrapper.GetControllerInfo();
                if (xinputInfo.Connected)
                {
                    return new ControllerState
                    {
                        IsConnected = true,
                        Name = xinputInfo.Name,
                        IsWireless = xinputInfo.IsWireless,
                        Source = DetectionSource.XInput
                    };
                }

                return new ControllerState
                {
                    IsConnected = false,
                    Source = DetectionSource.None
                };
            }

            // Windows.Gaming.Input first (latest API, best for modern controllers like DualSense)
            if (GamingInputWrapper.IsControllerConnected())
            {
                var name = GamingInputWrapper.GetControllerName();
                return new ControllerState
                {
                    IsConnected = true,
                    Name = name ?? "Game Controller",
                    IsWireless = false, // Can't reliably detect
                    Source = DetectionSource.WindowsGamingInput
                };
            }

            // XInput second (Xbox controllers)
            var xinputState = XInputWrapper.GetControllerInfo();
            if (xinputState.Connected)
            {
                return new ControllerState
                {
                    IsConnected = true,
                    Name = xinputState.Name,
                    IsWireless = xinputState.IsWireless,
                    Source = DetectionSource.XInput
                };
            }

            // HID enumeration last resort
            if (DirectInputWrapper.IsControllerConnected())
            {
                var controllers = DirectInputWrapper.GetConnectedControllerNames();
                string name = "Controller";
                if (controllers != null && controllers.Count > 0)
                {
                    name = controllers[0];
                }

                return new ControllerState
                {
                    IsConnected = true,
                    Name = name,
                    IsWireless = name.ToLowerInvariant().Contains("wireless"),
                    Source = DetectionSource.HidEnumeration
                };
            }

            return new ControllerState
            {
                IsConnected = false,
                Source = DetectionSource.None
            };
        }

        /// <summary>
        /// Get a simple controller name for display.
        /// </summary>
        public static string GetControllerName(bool xinputOnly = false)
        {
            var state = GetControllerState(xinputOnly);
            return state.IsConnected ? state.Name : null;
        }
    }
}
