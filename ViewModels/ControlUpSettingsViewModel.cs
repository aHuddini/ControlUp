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
                if (settings != null)
                {
                    settings.PropertyChanged -= Settings_PropertyChanged;
                }
                settings = value;
                if (settings != null)
                {
                    settings.PropertyChanged += Settings_PropertyChanged;
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(LongPressSliderEnabled));
            }
        }

        private void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ControlUpSettings.EnableHotkey) ||
                e.PropertyName == nameof(ControlUpSettings.RequireLongPress))
            {
                OnPropertyChanged(nameof(LongPressSliderEnabled));
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

        // Computed property for long press slider enabled state
        public bool LongPressSliderEnabled => Settings?.EnableHotkey == true && Settings?.RequireLongPress == true;

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

                // XInput Detection (Xbox controllers, wired or wireless via Xbox adapter)
                sb.AppendLine("XInput (Xbox/XInput-compatible):");
                var xinputInfo = XInputWrapper.GetControllerInfo();
                if (xinputInfo.Connected)
                {
                    string wireless = xinputInfo.IsWireless ? " (Wireless)" : " (Wired)";
                    sb.AppendLine($"  {xinputInfo.Name}{wireless}");
                }
                else
                {
                    sb.AppendLine("  No controller detected");
                }

                sb.AppendLine();

                // SDL Detection (cross-platform, includes PlayStation via HIDAPI)
                sb.AppendLine("SDL (PlayStation/Generic):");
                try
                {
                    if (SdlControllerWrapper.Initialize())
                    {
                        var sdlControllerName = SdlControllerWrapper.GetControllerName();
                        if (!string.IsNullOrEmpty(sdlControllerName))
                        {
                            sb.AppendLine($"  {sdlControllerName}");
                        }
                        else
                        {
                            sb.AppendLine("  No controller detected");
                        }
                        // NOTE: Intentionally NOT calling Shutdown() here
                        // SDL_Quit() corrupts COM apartment state and breaks WPF dialogs
                    }
                    else
                    {
                        sb.AppendLine("  SDL not available");
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"  Error: {ex.Message}");
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
                        // NOTE: Intentionally NOT calling Shutdown() here
                        // SDL_Quit() corrupts COM apartment state and breaks WPF dialogs
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
