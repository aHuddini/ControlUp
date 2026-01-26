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

        // Style preset selection
        private NotificationStylePreset _selectedStylePreset = NotificationStylePreset.Custom;
        public NotificationStylePreset SelectedStylePreset
        {
            get => _selectedStylePreset;
            set
            {
                if (_selectedStylePreset != value)
                {
                    _selectedStylePreset = value;
                    OnPropertyChanged();
                    if (value != NotificationStylePreset.Custom)
                    {
                        ApplyStylePreset(value);
                    }
                }
            }
        }

        public IEnumerable<NotificationStylePreset> AvailableStylePresets => new[]
        {
            NotificationStylePreset.Custom,
            NotificationStylePreset.MidnightBlue,
            NotificationStylePreset.DeepPurple,
            NotificationStylePreset.ForestGreen,
            NotificationStylePreset.CrimsonRed,
            NotificationStylePreset.SunsetOrange,
            NotificationStylePreset.OceanTeal,
            NotificationStylePreset.CharcoalGray,
            NotificationStylePreset.RosePink
        };

        private void ApplyStylePreset(NotificationStylePreset preset)
        {
            // All presets use acrylic blur, keep corner radius at 0
            Settings.EnableBlur = true;
            Settings.BlurMode = 1; // Acrylic

            switch (preset)
            {
                case NotificationStylePreset.MidnightBlue:
                    // Default style - deep blue
                    Settings.BlurTintColor = "00106C";
                    Settings.BlurOpacity = 49;
                    Settings.BackgroundColor = "071134";
                    Settings.BackgroundOpacity = 138;
                    Settings.BorderColor = "313553";
                    Settings.BorderThickness = 1;
                    break;

                case NotificationStylePreset.DeepPurple:
                    // Rich purple tones
                    Settings.BlurTintColor = "4A148C";
                    Settings.BlurOpacity = 60;
                    Settings.BackgroundColor = "1A0033";
                    Settings.BackgroundOpacity = 140;
                    Settings.BorderColor = "7B1FA2";
                    Settings.BorderThickness = 1;
                    break;

                case NotificationStylePreset.ForestGreen:
                    // Natural green
                    Settings.BlurTintColor = "1B5E20";
                    Settings.BlurOpacity = 55;
                    Settings.BackgroundColor = "0D2818";
                    Settings.BackgroundOpacity = 145;
                    Settings.BorderColor = "2E7D32";
                    Settings.BorderThickness = 1;
                    break;

                case NotificationStylePreset.CrimsonRed:
                    // Bold red
                    Settings.BlurTintColor = "8B0000";
                    Settings.BlurOpacity = 50;
                    Settings.BackgroundColor = "1A0505";
                    Settings.BackgroundOpacity = 150;
                    Settings.BorderColor = "B71C1C";
                    Settings.BorderThickness = 1;
                    break;

                case NotificationStylePreset.SunsetOrange:
                    // Warm orange
                    Settings.BlurTintColor = "E65100";
                    Settings.BlurOpacity = 45;
                    Settings.BackgroundColor = "1A0F00";
                    Settings.BackgroundOpacity = 155;
                    Settings.BorderColor = "FF6D00";
                    Settings.BorderThickness = 1;
                    break;

                case NotificationStylePreset.OceanTeal:
                    // Cool teal
                    Settings.BlurTintColor = "006064";
                    Settings.BlurOpacity = 55;
                    Settings.BackgroundColor = "001A1A";
                    Settings.BackgroundOpacity = 140;
                    Settings.BorderColor = "00838F";
                    Settings.BorderThickness = 1;
                    break;

                case NotificationStylePreset.CharcoalGray:
                    // Neutral dark gray
                    Settings.BlurTintColor = "212121";
                    Settings.BlurOpacity = 65;
                    Settings.BackgroundColor = "0A0A0A";
                    Settings.BackgroundOpacity = 160;
                    Settings.BorderColor = "424242";
                    Settings.BorderThickness = 1;
                    break;

                case NotificationStylePreset.RosePink:
                    // Soft pink
                    Settings.BlurTintColor = "880E4F";
                    Settings.BlurOpacity = 50;
                    Settings.BackgroundColor = "1A0510";
                    Settings.BackgroundOpacity = 145;
                    Settings.BorderColor = "AD1457";
                    Settings.BorderThickness = 1;
                    break;
            }
        }

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
                sb.AppendLine("XInput (Xbox or other compatible gamepads):");
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
                // Note: SDL stays initialized for plugin lifetime to avoid COM corruption
                sb.AppendLine("SDL (PlayStation or other compatible gamepads):");
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
                        // Don't call Shutdown() - SDL_Quit corrupts COM apartment state
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

                // HID Detection (shows all detected game controllers and adapters)
                sb.AppendLine();
                sb.AppendLine("HID (Gamepads + Misc Devices):");
                try
                {
                    var hidControllers = DirectInputWrapper.GetConnectedControllerNames();
                    if (hidControllers.Count > 0)
                    {
                        foreach (var controller in hidControllers)
                        {
                            sb.AppendLine($"  {controller}");
                        }
                    }
                    else
                    {
                        sb.AppendLine("  No devices detected");
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
                // Note: SDL stays initialized for plugin lifetime to avoid COM corruption
                var controllerName = XInputWrapper.GetControllerName();
                if (string.IsNullOrEmpty(controllerName))
                {
                    if (SdlControllerWrapper.Initialize())
                    {
                        controllerName = SdlControllerWrapper.GetControllerName();
                        // Don't call Shutdown() - SDL_Quit corrupts COM apartment state
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
