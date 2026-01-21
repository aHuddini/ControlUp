using System;
using System.IO;
using System.Runtime.InteropServices;
using SDL2;

namespace ControlUp.Common
{
    /// <summary>SDL2 wrapper for controller input - consistent with Playnite's fullscreen mode.</summary>
    public static class SdlControllerWrapper
    {
        private static readonly object _lock = new object();
        private static bool _initialized = false;
        private static bool _isAvailable = false;
        private static IntPtr _gameController = IntPtr.Zero;
        private static int _controllerIndex = -1;

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
                        return false;
                    }

                    // Try to load Playnite's gamecontrollerdb.txt if available
                    TryLoadControllerDatabase();

                    _isAvailable = true;
                    return true;
                }
                catch
                {
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
                    // Process events to update controller state
                    SDL.SDL_PumpEvents();

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
                    SDL.SDL_PumpEvents();

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
                SDL.SDL_PumpEvents();

                // Already have a valid controller?
                if (_gameController != IntPtr.Zero && _controllerIndex >= 0)
                {
                    if (SDL.SDL_GameControllerGetAttached(_gameController) == SDL.SDL_bool.SDL_TRUE)
                    {
                        return true;
                    }
                    // Controller was disconnected
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
                            _controllerIndex = i;
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // SDL call failed
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
                try
                {
                    SDL.SDL_GameControllerClose(_gameController);
                }
                catch { }
                _gameController = IntPtr.Zero;
                _controllerIndex = -1;
            }
        }

        /// <summary>Get current input state from the open controller.</summary>
        public static SdlControllerReading GetCurrentReading()
        {
            var result = new SdlControllerReading { IsValid = false };

            lock (_lock)
            {
                if (!_isAvailable) return result;
                if (!OpenControllerInternal()) return result;

                try
                {
                    // Process events to get latest state
                    SDL.SDL_PumpEvents();

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
                }
                catch
                {
                    // SDL call failed
                    CloseControllerInternal();
                }
            }
            return result;
        }
    }
}
