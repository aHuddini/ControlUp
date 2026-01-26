using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using SDL2;

namespace ControlUp.Common
{
    /// <summary>SDL2 wrapper for controller input - consistent with Playnite's fullscreen mode.</summary>
    public static class SdlControllerWrapper
    {
        // Static logger for diagnostics
        public static FileLogger Logger { get; set; }

        private static readonly object _lock = new object();
        private static bool _initialized = false;
        private static bool _isAvailable = false;
        private static IntPtr _gameController = IntPtr.Zero;
        private static int _controllerInstanceId = -1; // Track by instance ID, not joystick index

        // Tracking for diagnostics
        private static int _updateCount = 0;
        private static int _getCurrentReadingCount = 0;
        private static int _openControllerCount = 0;
        private static int _closeControllerCount = 0;
        private static DateTime _lastStatsLog = DateTime.MinValue;

        // Caching to reduce SDL call frequency
        private static SdlControllerReading _cachedReading = null;
        private static DateTime _lastReadingTime = DateTime.MinValue;
        private const int SDL_READING_CACHE_MS = 16; // ~60 FPS like Playnite

        // Lock timeout to prevent deadlocks if SDL hangs
        private const int LOCK_TIMEOUT_MS = 100;

        /// <summary>Controller button flags matching SDL_GameControllerButton.</summary>
        [Flags]
        public enum SdlButtons : uint
        {
            None = 0,
            A = 1 << 0,
            B = 1 << 1,
            X = 1 << 2,
            Y = 1 << 3,
            Back = 1 << 4,
            Guide = 1 << 5,
            Start = 1 << 6,
            LeftStick = 1 << 7,
            RightStick = 1 << 8,
            LeftShoulder = 1 << 9,
            RightShoulder = 1 << 10,
            DPadUp = 1 << 11,
            DPadDown = 1 << 12,
            DPadLeft = 1 << 13,
            DPadRight = 1 << 14,
            Misc1 = 1 << 15,
            Paddle1 = 1 << 16,
            Paddle2 = 1 << 17,
            Paddle3 = 1 << 18,
            Paddle4 = 1 << 19,
            Touchpad = 1 << 20
        }

        /// <summary>Controller input state from SDL.</summary>
        public class SdlControllerReading
        {
            public bool IsValid { get; set; }
            public SdlButtons Buttons { get; set; }
            public short LeftStickX { get; set; }   // -32768 to 32767
            public short LeftStickY { get; set; }
            public short RightStickX { get; set; }
            public short RightStickY { get; set; }
            public short LeftTrigger { get; set; }  // 0 to 32767
            public short RightTrigger { get; set; }
        }

        /// <summary>Whether SDL is available.</summary>
        public static bool IsAvailable => _isAvailable;

        /// <summary>Initialize SDL game controller subsystem.</summary>
        public static bool Initialize()
        {
            lock (_lock)
            {
                if (_initialized) return _isAvailable;
                _initialized = true;

                try
                {
                    // Initialize SDL with game controller support
                    if (SDL.SDL_Init(SDL.SDL_INIT_GAMECONTROLLER | SDL.SDL_INIT_JOYSTICK) < 0)
                    {
                        Logger?.Error($"[SDL] SDL_Init failed: {SDL.SDL_GetError()}");
                        return false;
                    }

                    // Try to load Playnite's gamecontrollerdb.txt if available
                    TryLoadControllerDatabase();

                    // Disable RawInput for joysticks (Playnite workaround for controller issues)
                    // https://github.com/libsdl-org/SDL/issues/13047
                    SDL.SDL_SetHint("SDL_JOYSTICK_RAWINPUT", "0");

                    // Disable Windows.Gaming.Input - it creates threads that leak TLS
                    // https://github.com/libsdl-org/SDL/issues/13291
                    SDL.SDL_SetHint("SDL_JOYSTICK_WGI", "0");

                    // Enable HIDAPI - required for PlayStation controllers (DualSense, DualShock 4)
                    SDL.SDL_SetHint("SDL_JOYSTICK_HIDAPI", "1");
                    SDL.SDL_SetHint("SDL_JOYSTICK_HIDAPI_PS4", "1");
                    SDL.SDL_SetHint("SDL_JOYSTICK_HIDAPI_PS5", "1");

                    // Disable SDL game controller events - we poll manually like Playnite does
                    SDL.SDL_GameControllerEventState(SDL.SDL_IGNORE);

                    _isAvailable = true;
                    Logger?.Info("[SDL] Initialized with HIDAPI enabled for PlayStation controllers");
                    return true;
                }
                catch (Exception ex)
                {
                    Logger?.Error($"[SDL] Initialize failed: {ex.Message}");
                    _isAvailable = false;
                    return false;
                }
            }
        }

        private static void TryLoadControllerDatabase()
        {
            try
            {
                // Check common Playnite install locations for gamecontrollerdb.txt
                string[] possiblePaths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Playnite", "gamecontrollerdb.txt"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Playnite", "gamecontrollerdb.txt"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Playnite", "gamecontrollerdb.txt"),
                };

                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        SDL.SDL_GameControllerAddMappingsFromFile(path);
                        break;
                    }
                }
            }
            catch
            {
                // Ignore - will use built-in mappings
            }
        }

        /// <summary>Shutdown SDL.</summary>
        public static void Shutdown()
        {
            lock (_lock)
            {
                CloseControllerInternal();
                if (_initialized && _isAvailable)
                {
                    SDL.SDL_Quit();
                }
                _initialized = false;
                _isAvailable = false;
            }
        }

        /// <summary>Check if any game controller is connected.</summary>
        public static bool IsControllerConnected()
        {
            lock (_lock)
            {
                if (!_isAvailable && !Initialize()) return false;

                try
                {
                    // Update controller state (like Playnite does)
                    SDL.SDL_GameControllerUpdate();

                    int numJoysticks = SDL.SDL_NumJoysticks();
                    for (int i = 0; i < numJoysticks; i++)
                    {
                        if (SDL.SDL_IsGameController(i) == SDL.SDL_bool.SDL_TRUE)
                        {
                            return true;
                        }
                    }
                }
                catch
                {
                    // SDL call failed
                }
                return false;
            }
        }

        /// <summary>Get the name of the first connected controller.</summary>
        public static string GetControllerName()
        {
            lock (_lock)
            {
                if (!_isAvailable && !Initialize()) return null;

                try
                {
                    SDL.SDL_GameControllerUpdate();

                    int numJoysticks = SDL.SDL_NumJoysticks();
                    for (int i = 0; i < numJoysticks; i++)
                    {
                        if (SDL.SDL_IsGameController(i) == SDL.SDL_bool.SDL_TRUE)
                        {
                            return SDL.SDL_GameControllerNameForIndex(i);
                        }
                    }
                }
                catch
                {
                    // SDL call failed
                }
                return null;
            }
        }

        /// <summary>Open the first available game controller for input reading.</summary>
        public static bool OpenController()
        {
            lock (_lock)
            {
                return OpenControllerInternal();
            }
        }

        private static bool OpenControllerInternal()
        {
            if (!_isAvailable) return false;

            try
            {
                // Already have a valid controller? Check by instance ID (stable identifier)
                if (_gameController != IntPtr.Zero && _controllerInstanceId >= 0)
                {
                    if (SDL.SDL_GameControllerGetAttached(_gameController) == SDL.SDL_bool.SDL_TRUE)
                    {
                        return true;
                    }
                    // Controller was disconnected
                    Logger?.Debug($"[SDL] Controller instance {_controllerInstanceId} disconnected, closing handle");
                    CloseControllerInternal();
                }

                // Find and open a controller
                int numJoysticks = SDL.SDL_NumJoysticks();
                for (int i = 0; i < numJoysticks; i++)
                {
                    if (SDL.SDL_IsGameController(i) == SDL.SDL_bool.SDL_TRUE)
                    {
                        _gameController = SDL.SDL_GameControllerOpen(i);
                        if (_gameController != IntPtr.Zero)
                        {
                            // Get the joystick and its instance ID (stable across re-enumeration)
                            var joystick = SDL.SDL_GameControllerGetJoystick(_gameController);
                            _controllerInstanceId = SDL.SDL_JoystickInstanceID(joystick);
                            _openControllerCount++;
                            Logger?.Debug($"[SDL] Opened controller #{_openControllerCount} at index {i}, instanceId={_controllerInstanceId}, handle=0x{_gameController.ToInt64():X}");
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.Error($"[SDL] OpenControllerInternal failed: {ex.Message}");
            }
            return false;
        }

        /// <summary>Close the currently open controller.</summary>
        public static void CloseController()
        {
            lock (_lock)
            {
                CloseControllerInternal();
            }
        }

        private static void CloseControllerInternal()
        {
            if (_gameController != IntPtr.Zero)
            {
                _closeControllerCount++;
                Logger?.Debug($"[SDL] Closing controller #{_closeControllerCount}, instanceId={_controllerInstanceId}, handle=0x{_gameController.ToInt64():X}");
                try
                {
                    SDL.SDL_GameControllerClose(_gameController);
                }
                catch (Exception ex)
                {
                    Logger?.Error($"[SDL] SDL_GameControllerClose failed: {ex.Message}");
                }
                _gameController = IntPtr.Zero;
                _controllerInstanceId = -1;
                _cachedReading = null; // Invalidate cache when controller closes
            }
        }

        /// <summary>Get current input state from the open controller.</summary>
        public static SdlControllerReading GetCurrentReading()
        {
            var result = new SdlControllerReading { IsValid = false };

            // Use timeout to prevent deadlocks if SDL hangs
            if (!Monitor.TryEnter(_lock, LOCK_TIMEOUT_MS))
            {
                Logger?.Debug("[SDL] GetCurrentReading: Lock timeout, skipping this iteration");
                return result;
            }

            try
            {
                _getCurrentReadingCount++;

                // Log stats every 30 seconds
                if ((DateTime.Now - _lastStatsLog).TotalSeconds >= 30)
                {
                    _lastStatsLog = DateTime.Now;
                    Logger?.Info($"[SDL] Stats: GetCurrentReading={_getCurrentReadingCount}, Updates={_updateCount}, Open={_openControllerCount}, Close={_closeControllerCount}");
                }

                // Return cached reading if still fresh (reduces SDL call frequency)
                var now = DateTime.Now;
                if (_cachedReading != null && _cachedReading.IsValid &&
                    (now - _lastReadingTime).TotalMilliseconds < SDL_READING_CACHE_MS)
                {
                    return _cachedReading;
                }

                if (!_isAvailable) return result;
                if (!OpenControllerInternal()) return result;

                try
                {
                    // Update controller state (Playnite uses SDL_GameControllerUpdate, not PumpEvents)
                    _updateCount++;
                    SDL.SDL_GameControllerUpdate();

                    // Check if still attached
                    if (SDL.SDL_GameControllerGetAttached(_gameController) != SDL.SDL_bool.SDL_TRUE)
                    {
                        CloseControllerInternal();
                        return result;
                    }

                    // Read buttons
                    SdlButtons buttons = SdlButtons.None;

                    if (SDL.SDL_GameControllerGetButton(_gameController, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A) == 1)
                        buttons |= SdlButtons.A;
                    if (SDL.SDL_GameControllerGetButton(_gameController, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B) == 1)
                        buttons |= SdlButtons.B;
                    if (SDL.SDL_GameControllerGetButton(_gameController, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_X) == 1)
                        buttons |= SdlButtons.X;
                    if (SDL.SDL_GameControllerGetButton(_gameController, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_Y) == 1)
                        buttons |= SdlButtons.Y;
                    if (SDL.SDL_GameControllerGetButton(_gameController, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_BACK) == 1)
                        buttons |= SdlButtons.Back;
                    if (SDL.SDL_GameControllerGetButton(_gameController, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_GUIDE) == 1)
                        buttons |= SdlButtons.Guide;
                    if (SDL.SDL_GameControllerGetButton(_gameController, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_START) == 1)
                        buttons |= SdlButtons.Start;
                    if (SDL.SDL_GameControllerGetButton(_gameController, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSTICK) == 1)
                        buttons |= SdlButtons.LeftStick;
                    if (SDL.SDL_GameControllerGetButton(_gameController, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSTICK) == 1)
                        buttons |= SdlButtons.RightStick;
                    if (SDL.SDL_GameControllerGetButton(_gameController, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER) == 1)
                        buttons |= SdlButtons.LeftShoulder;
                    if (SDL.SDL_GameControllerGetButton(_gameController, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSHOULDER) == 1)
                        buttons |= SdlButtons.RightShoulder;
                    if (SDL.SDL_GameControllerGetButton(_gameController, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP) == 1)
                        buttons |= SdlButtons.DPadUp;
                    if (SDL.SDL_GameControllerGetButton(_gameController, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN) == 1)
                        buttons |= SdlButtons.DPadDown;
                    if (SDL.SDL_GameControllerGetButton(_gameController, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT) == 1)
                        buttons |= SdlButtons.DPadLeft;
                    if (SDL.SDL_GameControllerGetButton(_gameController, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT) == 1)
                        buttons |= SdlButtons.DPadRight;

                    result.Buttons = buttons;

                    // Read axes
                    result.LeftStickX = SDL.SDL_GameControllerGetAxis(_gameController, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX);
                    result.LeftStickY = SDL.SDL_GameControllerGetAxis(_gameController, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY);
                    result.RightStickX = SDL.SDL_GameControllerGetAxis(_gameController, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTX);
                    result.RightStickY = SDL.SDL_GameControllerGetAxis(_gameController, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTY);
                    result.LeftTrigger = SDL.SDL_GameControllerGetAxis(_gameController, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERLEFT);
                    result.RightTrigger = SDL.SDL_GameControllerGetAxis(_gameController, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERRIGHT);

                    result.IsValid = true;

                    // Cache the reading
                    _cachedReading = result;
                    _lastReadingTime = now;
                }
                catch
                {
                    // SDL call failed
                    CloseControllerInternal();
                }
            }
            finally
            {
                Monitor.Exit(_lock);
            }
            return result;
        }

        /// <summary>Invalidate the cached reading (call when controller state might have changed).</summary>
        public static void InvalidateCache()
        {
            lock (_lock)
            {
                _cachedReading = null;
            }
        }
    }
}
