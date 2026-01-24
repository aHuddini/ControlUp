using System;

namespace ControlUp.Common
{
    /// <summary>
    /// Unified controller detection using XInput and DirectInput (HID).
    /// XInput: Xbox controllers and XInput-compatible devices (fast, no handle leaks)
    /// DirectInput: All other controllers including DualSense, DualShock, etc. (checked less frequently)
    /// SDL is used separately for reading input from non-XInput controllers.
    /// </summary>
    public static class ControllerDetector
    {
        public enum DetectionSource
        {
            None,
            XInput,
            DirectInput
        }

        public class ControllerState
        {
            public bool IsConnected { get; set; }
            public string Name { get; set; }
            public bool IsWireless { get; set; }
            public DetectionSource Source { get; set; }
        }

        // Static logger for diagnostics
        public static FileLogger Logger { get; set; }

        // Cache for DirectInput detection to balance responsiveness vs handle usage
        // DirectInput/HID enumeration creates handles on each call
        private static ControllerState _cachedDirectInputState = null;
        private static DateTime _lastDirectInputCheck = DateTime.MinValue;
        private const int DIRECTINPUT_CHECK_INTERVAL_MS = 1000; // Check every 1 second for responsive detection

        // Track call counts for diagnostics
        private static int _getControllerStateCallCount = 0;
        private static int _directInputRefreshCount = 0;

        /// <summary>
        /// Check if any XInput controller is connected.
        /// </summary>
        public static bool IsXInputControllerConnected()
        {
            return XInputWrapper.IsControllerConnected();
        }

        /// <summary>
        /// Check if any controller is connected using XInput and DirectInput.
        /// XInput is checked every time (no handle leaks).
        /// DirectInput uses the same cache as GetControllerState to avoid double enumeration.
        /// </summary>
        public static bool IsAnyControllerConnected()
        {
            // XInput first - always safe to call frequently
            if (XInputWrapper.IsControllerConnected())
                return true;

            // Use GetControllerState to leverage single cache (avoids double enumeration)
            var state = GetControllerState(xinputOnly: false);
            return state.IsConnected;
        }

        /// <summary>
        /// Get full controller state with name and detection source.
        /// XInput is checked every time. DirectInput uses cached results.
        /// </summary>
        public static ControllerState GetControllerState(bool xinputOnly = false)
        {
            _getControllerStateCallCount++;

            // Always check XInput first - it's fast and doesn't leak handles
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

            // If XInput-only mode, don't check DirectInput
            if (xinputOnly)
            {
                return new ControllerState
                {
                    IsConnected = false,
                    Source = DetectionSource.None
                };
            }

            // For non-XInput controllers, use DirectInput with caching
            var now = DateTime.Now;
            double msSinceLastCheck = (now - _lastDirectInputCheck).TotalMilliseconds;
            bool needsRefresh = msSinceLastCheck >= DIRECTINPUT_CHECK_INTERVAL_MS;

            if (needsRefresh)
            {
                _directInputRefreshCount++;
                Logger?.Info($"[ControllerDetector] DirectInput REFRESH #{_directInputRefreshCount} (call #{_getControllerStateCallCount}, {msSinceLastCheck:F0}ms since last check)");
                _lastDirectInputCheck = now;
                _cachedDirectInputState = GetDirectInputControllerState();
            }

            // Log stats every 100 calls
            if (_getControllerStateCallCount % 100 == 0)
            {
                Logger?.Debug($"[ControllerDetector] Stats: {_getControllerStateCallCount} calls, {_directInputRefreshCount} DirectInput refreshes");
            }

            return _cachedDirectInputState ?? new ControllerState { IsConnected = false, Source = DetectionSource.None };
        }

        /// <summary>
        /// Internal method to check DirectInput/HID controllers.
        /// Single enumeration to avoid double handle creation.
        /// </summary>
        private static ControllerState GetDirectInputControllerState()
        {
            try
            {
                // Single call to get controller names - avoids double enumeration
                // GetConnectedControllerNames already does the full HID enumeration
                var controllers = DirectInputWrapper.GetConnectedControllerNames();
                if (controllers != null && controllers.Count > 0)
                {
                    string name = controllers[0];
                    return new ControllerState
                    {
                        IsConnected = true,
                        Name = name,
                        IsWireless = name.ToLowerInvariant().Contains("wireless") ||
                                     name.ToLowerInvariant().Contains("bluetooth"),
                        Source = DetectionSource.DirectInput
                    };
                }
            }
            catch
            {
                // DirectInput detection failed
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
