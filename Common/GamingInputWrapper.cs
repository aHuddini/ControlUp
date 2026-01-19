using System;
using System.Linq;

namespace ControlUp.Common
{
    /// <summary>
    /// Wrapper for Windows.Gaming.Input API to detect Xbox and other controllers (including Bluetooth)
    /// </summary>
    public static class GamingInputWrapper
    {
        private static dynamic _gamepadClass;
        private static dynamic _gamepadsProperty;

        static GamingInputWrapper()
        {
            try
            {
                // Try different assembly names for Windows.Gaming.Input
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
                                Console.WriteLine($"ControlUp: Successfully loaded Windows.Gaming.Input using {assemblyName}");
                                break;
                            }
                        }
                    }
                    catch
                    {
                        // Try next assembly name
                        continue;
                    }
                }

                if (_gamepadClass == null || _gamepadsProperty == null)
                {
                    Console.WriteLine("ControlUp: Windows.Gaming.Input not available on this system");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ControlUp: Error initializing Windows.Gaming.Input: {ex.Message}");
                _gamepadClass = null;
                _gamepadsProperty = null;
            }
        }

        /// <summary>
        /// Checks if any game controller is currently connected via Windows.Gaming.Input
        /// </summary>
        public static bool IsControllerConnected()
        {
            try
            {
                // First check if Windows.Gaming.Input is available
                if (_gamepadClass == null || _gamepadsProperty == null)
                {
                    Console.WriteLine("ControlUp: Windows.Gaming.Input not available");
                    return false;
                }

                // Get the gamepads collection
                var gamepads = (System.Collections.Generic.IReadOnlyList<dynamic>)_gamepadsProperty.GetValue(null);

                if (gamepads != null && gamepads.Count > 0)
                {
                    Console.WriteLine($"ControlUp: Found {gamepads.Count} gamepad(s) via Windows.Gaming.Input");
                    return true;
                }
                else
                {
                    Console.WriteLine("ControlUp: No gamepads found via Windows.Gaming.Input");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ControlUp: Error accessing Windows.Gaming.Input: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Advanced Xbox controller detection using multiple methods
        /// </summary>
        public static bool IsXboxControllerConnected()
        {
            // First try Windows.Gaming.Input (most reliable)
            if (IsControllerConnected())
            {
                return true;
            }

            // Fallback: Try Windows.Devices.Enumeration for gaming devices
            try
            {
                // Use reflection to access Windows.Devices.Enumeration
                var devicesAssembly = System.Reflection.Assembly.Load("System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
                if (devicesAssembly != null)
                {
                    var deviceInformationType = devicesAssembly.GetType("Windows.Devices.Enumeration.DeviceInformation");
                    if (deviceInformationType != null)
                    {
                        // Try to find devices with gaming interface class
                        var findAllAsyncMethod = deviceInformationType.GetMethod("FindAllAsync", new[] { typeof(string) });
                        if (findAllAsyncMethod != null)
                        {
                            // AQS query for gaming devices
                            string aqsFilter = "System.Devices.InterfaceClassGuid:=\"{4d1e55b2-f16f-11cf-88cb-001111000030}\" AND System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True";

                            try
                            {
                                var task = (dynamic)findAllAsyncMethod.Invoke(null, new object[] { aqsFilter });
                                if (task != null)
                                {
                                    // This is async, but we'll try to get the result synchronously
                                    var result = task.GetAwaiter().GetResult();
                                    if (result != null && result.Count > 0)
                                    {
                                        Console.WriteLine($"ControlUp: Found {result.Count} gaming devices via Windows.Devices.Enumeration");
                                        return true;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"ControlUp: Error enumerating gaming devices: {ex.Message}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ControlUp: Windows.Devices.Enumeration not available: {ex.Message}");
            }

            // Final fallback: Check for known Xbox Bluetooth service UUIDs in HID devices
            try
            {
                // Xbox controllers use specific Bluetooth service UUIDs
                // The HID Service UUID (00001812-0000-1000-8000-00805f9b34fb) is common
                // But Xbox controllers also advertise additional services

                // This is a simplified check - look for devices that are likely Xbox controllers
                var hidDevices = DirectInputWrapper.GetConnectedControllers();
                foreach (var device in hidDevices)
                {
                    string deviceLower = device.ToLowerInvariant();

                    // Look for Xbox-specific patterns in device names or paths
                    if (deviceLower.Contains("xbox") ||
                        deviceLower.Contains("wireless") ||
                        deviceLower.Contains("045e") || // Microsoft VID
                        deviceLower.Contains("00001812")) // HID Service UUID
                    {
                        Console.WriteLine($"ControlUp: Found potential Xbox controller via HID enumeration: {device}");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ControlUp: Error checking HID devices for Xbox controllers: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Gets detailed information about connected controllers via Windows.Gaming.Input
        /// </summary>
        public static System.Collections.Generic.List<string> GetConnectedControllerInfo()
        {
            var controllers = new System.Collections.Generic.List<string>();

            try
            {
                if (_gamepadClass == null || _gamepadsProperty == null)
                {
                    controllers.Add("Windows.Gaming.Input not available");
                    return controllers;
                }

                var gamepads = (System.Collections.Generic.IReadOnlyList<dynamic>)_gamepadsProperty.GetValue(null);

                if (gamepads != null && gamepads.Count > 0)
                {
                    for (int i = 0; i < gamepads.Count; i++)
                    {
                        try
                        {
                            var gamepad = gamepads[i];

                            // Try to get additional info if available
                            string controllerInfo = $"Xbox Gamepad {i + 1}";

                            // Try to access properties like Id, IsWireless, etc.
                            try
                            {
                                var idProperty = _gamepadClass.GetProperty("Id");
                                if (idProperty != null)
                                {
                                    var id = idProperty.GetValue(gamepad);
                                    controllerInfo += $" (ID: {id})";
                                }
                            }
                            catch { }

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
                        catch (Exception ex)
                        {
                            controllers.Add($"Xbox Gamepad {i + 1} (Error reading details: {ex.Message})");
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
                controllers.Add($"Error accessing Windows.Gaming.Input: {ex.Message}");
            }

            return controllers;
        }
    }
}