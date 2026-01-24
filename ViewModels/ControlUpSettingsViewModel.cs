using Playnite.SDK;
using Playnite.SDK.Data;
using ControlUp.Common;
using ControlUp.Dialogs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;

namespace ControlUp
{
    public class ControlUpSettingsViewModel : ObservableObject, ISettings
    {
        public readonly ILogger Logger = LogManager.GetLogger();
        public IPlayniteAPI PlayniteApi { get; set; }
        public ControlUpPlugin Plugin { get; set; }
        public ControlUpSettings EditingClone { get; set; }

        private ControlUpSettings settings;
        public ControlUpSettings Settings
        {
            get => settings;
            set
            {
                settings = value;
                OnPropertyChanged();
            }
        }

        // Filtered list of trigger modes for UI (excludes legacy values)
        public IEnumerable<FullscreenTriggerMode> AvailableTriggerModes => new[]
        {
            FullscreenTriggerMode.Disabled,
            FullscreenTriggerMode.NewConnectionOnly,
            FullscreenTriggerMode.AnyControllerAnytime,
            FullscreenTriggerMode.StartupOnly
        };

        public ControlUpSettingsViewModel(ControlUpPlugin plugin, IPlayniteAPI playniteApi)
        {
            Plugin = plugin;
            PlayniteApi = playniteApi;

            var savedSettings = Plugin.LoadPluginSettings<ControlUpSettings>();
            Settings = savedSettings ?? new ControlUpSettings();
        }

        public void BeginEdit()
        {
            EditingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            Settings = EditingClone;
        }

        public void EndEdit()
        {
            Plugin.SavePluginSettings(Settings);
            Plugin.OnSettingsChanged();
        }

        public virtual bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            return true;
        }

        public string DetectedControllersText { get; set; } = "Click 'Detect Controllers' to scan for connected controllers.";

        public RelayCommand DetectControllersCommand => new RelayCommand(() =>
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Scanning for connected controllers...\n");

                var controllers = new List<string>();

                // Check XInput (Xbox controllers)
                var xinputInfo = XInputWrapper.GetControllerInfo();
                if (xinputInfo.Connected)
                {
                    string wireless = xinputInfo.IsWireless ? " [Wireless]" : "";
                    controllers.Add($"{xinputInfo.Name}{wireless} [XInput]");
                }

                // Check SDL (all controller types including PlayStation)
                string sdlControllerName = null;
                try
                {
                    if (SdlControllerWrapper.Initialize())
                    {
                        sdlControllerName = SdlControllerWrapper.GetControllerName();
                        if (!string.IsNullOrEmpty(sdlControllerName))
                        {
                            // Only add if it's not an Xbox controller (avoid duplicates)
                            bool isXboxController = sdlControllerName.IndexOf("Xbox", StringComparison.OrdinalIgnoreCase) >= 0;
                            if (!isXboxController || !xinputInfo.Connected)
                            {
                                controllers.Add($"{sdlControllerName} [SDL]");
                            }
                        }
                        SdlControllerWrapper.Shutdown();
                    }
                }
                catch { }

                // Check DirectInput/HID as fallback (PlayStation controllers)
                // Only if SDL didn't find a PlayStation controller
                bool sdlFoundPlayStation = !string.IsNullOrEmpty(sdlControllerName) &&
                    (sdlControllerName.IndexOf("DualSense", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     sdlControllerName.IndexOf("DualShock", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     sdlControllerName.IndexOf("PS5", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     sdlControllerName.IndexOf("PS4", StringComparison.OrdinalIgnoreCase) >= 0);

                if (!sdlFoundPlayStation)
                {
                    try
                    {
                        var hidControllers = DirectInputWrapper.GetConnectedControllerNames();
                        foreach (var hidName in hidControllers)
                        {
                            // Only add PlayStation controllers not already detected
                            bool isPlayStation = hidName.IndexOf("DualSense", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                 hidName.IndexOf("DualShock", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                 hidName.IndexOf("Wireless Controller", StringComparison.OrdinalIgnoreCase) >= 0;
                            if (isPlayStation)
                            {
                                controllers.Add($"{hidName} [DirectInput]");
                            }
                        }
                    }
                    catch { }
                }

                if (controllers.Count > 0)
                {
                    sb.AppendLine($"Detected {controllers.Count} controller(s):");
                    foreach (var controller in controllers)
                    {
                        sb.AppendLine($"  - {controller}");
                    }
                }
                else
                {
                    sb.AppendLine("No controllers detected.");
                }

                DetectedControllersText = sb.ToString();
                OnPropertyChanged(nameof(DetectedControllersText));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to detect controllers");
                DetectedControllersText = $"Error detecting controllers: {ex.Message}";
                OnPropertyChanged(nameof(DetectedControllersText));
            }
        });

        public RelayCommand OpenLogsFolderCommand => new RelayCommand(() =>
        {
            try
            {
                string extensionPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                if (Directory.Exists(extensionPath))
                {
                    string logFile = Path.Combine(extensionPath, "ControlUp.log");
                    if (File.Exists(logFile))
                    {
                        Process.Start("explorer.exe", $"/select,\"{logFile}\"");
                    }
                    else
                    {
                        Process.Start("explorer.exe", extensionPath);
                    }
                }
                else
                {
                    PlayniteApi.Dialogs.ShowErrorMessage("Extension folder not found.", "Error");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to open extension folder");
                PlayniteApi.Dialogs.ShowErrorMessage($"Failed to open folder: {ex.Message}", "Error");
            }
        });

        public RelayCommand PreviewNotificationCommand => new RelayCommand(() =>
        {
            try
            {
                // Try to get controller name from XInput first, then SDL, then DirectInput
                var controllerName = XInputWrapper.GetControllerName();
                if (string.IsNullOrEmpty(controllerName))
                {
                    if (SdlControllerWrapper.Initialize())
                    {
                        controllerName = SdlControllerWrapper.GetControllerName();
                        SdlControllerWrapper.Shutdown();
                    }
                }
                if (string.IsNullOrEmpty(controllerName))
                {
                    var hidControllers = DirectInputWrapper.GetConnectedControllerNames();
                    if (hidControllers.Count > 0)
                    {
                        controllerName = hidControllers[0];
                    }
                }
                controllerName = controllerName ?? "Controller";

                var dialog = new ControllerDetectedDialog(Settings, FullscreenTriggerSource.Connection, controllerName);
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to preview notification");
                PlayniteApi.Dialogs.ShowErrorMessage($"Preview failed: {ex.Message}", "Error");
            }
        });
    }
}
