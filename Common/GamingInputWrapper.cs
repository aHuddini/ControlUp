using System;
using System.Collections.Generic;

namespace ControlUp.Common
{
    /// <summary>Windows.Gaming.Input API wrapper for Bluetooth controller detection.</summary>
    public static class GamingInputWrapper
    {
        private static dynamic _gamepadClass;
        private static dynamic _gamepadsProperty;

        static GamingInputWrapper()
        {
            try
            {
                string[] assemblyNames = new[]
                {
                    "Windows.Gaming.Input, Version=255.255.255.255, Culture=neutral, PublicKeyToken=0000000000000000, processorArchitecture=MSIL",
                    "Windows.Gaming.Input",
                    "Windows.Gaming.Input, Version=10.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35"
                };

                foreach (var assemblyName in assemblyNames)
                {
                    try
                    {
                        var gamingInputAssembly = System.Reflection.Assembly.Load(assemblyName);
                        if (gamingInputAssembly != null)
                        {
                            _gamepadClass = gamingInputAssembly.GetType("Windows.Gaming.Input.Gamepad");
                            _gamepadsProperty = _gamepadClass?.GetProperty("Gamepads");
                            if (_gamepadClass != null && _gamepadsProperty != null)
                            {
                                break;
                            }
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
            }
            catch
            {
                _gamepadClass = null;
                _gamepadsProperty = null;
            }
        }

        public static bool IsControllerConnected()
        {
            try
            {
                if (_gamepadClass == null || _gamepadsProperty == null)
                    return false;

                var gamepads = (IReadOnlyList<dynamic>)_gamepadsProperty.GetValue(null);
                return gamepads != null && gamepads.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsXboxControllerConnected()
        {
            if (IsControllerConnected())
                return true;

            // Fallback: Check HID devices for Xbox patterns
            try
            {
                var hidDevices = DirectInputWrapper.GetConnectedControllers();
                foreach (var device in hidDevices)
                {
                    string deviceLower = device.ToLowerInvariant();
                    if (deviceLower.Contains("xbox") ||
                        deviceLower.Contains("wireless") ||
                        deviceLower.Contains("045e"))  // Microsoft VID
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore HID enumeration errors
            }

            return false;
        }

        public static List<string> GetConnectedControllerInfo()
        {
            var controllers = new List<string>();

            try
            {
                if (_gamepadClass == null || _gamepadsProperty == null)
                {
                    controllers.Add("Windows.Gaming.Input not available");
                    return controllers;
                }

                var gamepads = (IReadOnlyList<dynamic>)_gamepadsProperty.GetValue(null);

                if (gamepads != null && gamepads.Count > 0)
                {
                    for (int i = 0; i < gamepads.Count; i++)
                    {
                        try
                        {
                            var gamepad = gamepads[i];
                            string controllerInfo = $"Xbox Gamepad {i + 1}";

                            try
                            {
                                var isWirelessProperty = _gamepadClass.GetProperty("IsWireless");
                                if (isWirelessProperty != null)
                                {
                                    var isWireless = (bool)isWirelessProperty.GetValue(gamepad);
                                    controllerInfo += $" ({(isWireless ? "Wireless" : "Wired")})";
                                }
                            }
                            catch { }

                            controllers.Add(controllerInfo);
                        }
                        catch
                        {
                            controllers.Add($"Xbox Gamepad {i + 1}");
                        }
                    }
                }
                else
                {
                    controllers.Add("No controllers detected");
                }
            }
            catch (Exception ex)
            {
                controllers.Add($"Error: {ex.Message}");
            }

            return controllers;
        }
    }
}
