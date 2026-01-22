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
            FullscreenTriggerMode.XInputControllerOnStartup,
            FullscreenTriggerMode.AnyControllerOnStartup,
            FullscreenTriggerMode.XInputController,
            FullscreenTriggerMode.AnyController
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

        public string DetectedControllersText { get; set; } = "Click 'Detect Controllers' to scan.";

        public RelayCommand DetectControllersCommand => new RelayCommand(() =>
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=== Controller Detection Results ===\n");

                // XInput detection (Xbox controllers)
                var xinputInfo = XInputWrapper.GetControllerInfo();
                sb.AppendLine("XInput (Xbox Controllers):");
                if (xinputInfo.Connected)
                {
                    string wireless = xinputInfo.IsWireless ? " [Wireless]" : "";
                    sb.AppendLine($"  ✓ {xinputInfo.Name}{wireless}");
                }
                else
                {
                    sb.AppendLine("  • No XInput controllers detected");
                }
                sb.AppendLine();

                // DirectInput/HID detection (PlayStation & other controllers)
                sb.AppendLine("DirectInput/HID (PlayStation & Other Controllers):");
                try
                {
                    var hidControllers = DirectInputWrapper.GetConnectedControllerNames();
                    if (hidControllers.Any())
                    {
                        foreach (var name in hidControllers.Take(5))
                        {
                            sb.AppendLine($"  ✓ {name}");
                        }
                        if (hidControllers.Count > 5)
                        {
                            sb.AppendLine($"  ... and {hidControllers.Count - 5} more");
                        }
                    }
                    else
                    {
                        sb.AppendLine("  • No HID game controllers detected");
                    }
                }
                catch (Exception hidEx)
                {
                    sb.AppendLine($"  • HID error: {hidEx.Message}");
                }
                sb.AppendLine();

                // SDL status (for input reading)
                sb.AppendLine("SDL (Input Reading):");
                try
                {
                    if (SdlControllerWrapper.Initialize() || SdlControllerWrapper.IsAvailable)
                    {
                        if (SdlControllerWrapper.IsControllerConnected())
                        {
                            var sdlName = SdlControllerWrapper.GetControllerName();
                            sb.AppendLine($"  ✓ Ready: {sdlName ?? "Game Controller"}");
                        }
                        else
                        {
                            sb.AppendLine("  • SDL initialized, no controller open");
                        }
                    }
                    else
                    {
                        sb.AppendLine("  • SDL not available");
                    }
                }
                catch (Exception sdlEx)
                {
                    sb.AppendLine($"  • SDL error: {sdlEx.Message}");
                }
                sb.AppendLine();

                // Summary
                sb.AppendLine("Summary:");
                var state = ControllerDetector.GetControllerState(false);
                if (state.IsConnected)
                {
                    sb.AppendLine($"  ✓ Active controller: {state.Name}");
                    sb.AppendLine($"    Detection source: {state.Source}");
                }
                else
                {
                    sb.AppendLine("  • No controllers currently detected");
                }

                DetectedControllersText = sb.ToString();
                OnPropertyChanged(nameof(DetectedControllersText));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to detect controllers");
                DetectedControllersText = $"Error: {ex.Message}";
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
                var controllerName = XInputWrapper.GetControllerName() ?? "Controller";
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
