using Playnite.SDK;
using Playnite.SDK.Data;
using ControlUp.Common;
using ControlUp.Dialogs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
            if (e.PropertyName == nameof(ControlUpSettings.LongPressDelayMs))
            {
                OnPropertyChanged(nameof(LongPressDelaySeconds));
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

        // Computed property for long press delay in seconds (for display)
        public string LongPressDelaySeconds => Settings != null ? $"({Settings.LongPressDelayMs / 1000.0:F2}s)" : "";

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

        // Toast style preset selection
        private ToastStylePreset _selectedToastStylePreset = ToastStylePreset.Custom;
        public ToastStylePreset SelectedToastStylePreset
        {
            get => _selectedToastStylePreset;
            set
            {
                if (_selectedToastStylePreset != value)
                {
                    _selectedToastStylePreset = value;
                    OnPropertyChanged();
                    if (value != ToastStylePreset.Custom)
                    {
                        ApplyToastStylePreset(value);
                    }
                }
            }
        }

        public IEnumerable<ToastStylePreset> AvailableToastStylePresets => new[]
        {
            ToastStylePreset.Custom,
            ToastStylePreset.OceanBlue,
            ToastStylePreset.MidnightPurple,
            ToastStylePreset.ForestGreen,
            ToastStylePreset.SunsetOrange,
            ToastStylePreset.CrimsonRed,
            ToastStylePreset.CharcoalGray,
            ToastStylePreset.RosePink,
            ToastStylePreset.OceanTeal
        };

        private void ApplyToastStylePreset(ToastStylePreset preset)
        {
            // All presets use acrylic blur with low opacity (40 or below) to show the blur effect
            Settings.EnableToastBlur = true;
            Settings.ToastBorderOpacity = 70;

            switch (preset)
            {
                case ToastStylePreset.OceanBlue:
                    // Default info style - blue accent
                    Settings.ToastBlurOpacity = 35;
                    Settings.ToastBlurTintColor = "0D1B2A";
                    Settings.ToastBorderColor = "1B3A5C";
                    Settings.ToastAccentColor = "64B5F6";
                    Settings.ToastTextColor = "E0E0E0";
                    break;

                case ToastStylePreset.MidnightPurple:
                    // Deep purple tones
                    Settings.ToastBlurOpacity = 38;
                    Settings.ToastBlurTintColor = "1A0A2E";
                    Settings.ToastBorderColor = "4A148C";
                    Settings.ToastAccentColor = "CE93D8";
                    Settings.ToastTextColor = "E0E0E0";
                    break;

                case ToastStylePreset.ForestGreen:
                    // Natural green
                    Settings.ToastBlurOpacity = 35;
                    Settings.ToastBlurTintColor = "0D1F12";
                    Settings.ToastBorderColor = "1B5E20";
                    Settings.ToastAccentColor = "81C784";
                    Settings.ToastTextColor = "E0E0E0";
                    break;

                case ToastStylePreset.SunsetOrange:
                    // Warm orange
                    Settings.ToastBlurOpacity = 32;
                    Settings.ToastBlurTintColor = "1A0F00";
                    Settings.ToastBorderColor = "E65100";
                    Settings.ToastAccentColor = "FFB74D";
                    Settings.ToastTextColor = "E0E0E0";
                    break;

                case ToastStylePreset.CrimsonRed:
                    // Bold red
                    Settings.ToastBlurOpacity = 35;
                    Settings.ToastBlurTintColor = "1A0505";
                    Settings.ToastBorderColor = "B71C1C";
                    Settings.ToastAccentColor = "FF8A80";
                    Settings.ToastTextColor = "E0E0E0";
                    break;

                case ToastStylePreset.CharcoalGray:
                    // Neutral dark gray
                    Settings.ToastBlurOpacity = 40;
                    Settings.ToastBlurTintColor = "121212";
                    Settings.ToastBorderColor = "424242";
                    Settings.ToastAccentColor = "BDBDBD";
                    Settings.ToastTextColor = "E0E0E0";
                    break;

                case ToastStylePreset.RosePink:
                    // Soft pink
                    Settings.ToastBlurOpacity = 35;
                    Settings.ToastBlurTintColor = "1A0510";
                    Settings.ToastBorderColor = "880E4F";
                    Settings.ToastAccentColor = "F48FB1";
                    Settings.ToastTextColor = "E0E0E0";
                    break;

                case ToastStylePreset.OceanTeal:
                    // Cool teal (default)
                    Settings.ToastBlurOpacity = 30;
                    Settings.ToastBlurTintColor = "001A1A";
                    Settings.ToastBorderColor = "00838F";
                    Settings.ToastAccentColor = "4DD0E1";
                    Settings.ToastTextColor = "E0E0E0";
                    break;
            }
        }

        private void ApplyStylePreset(NotificationStylePreset preset)
        {
            // All presets use acrylic blur with consistent opacity values
            Settings.EnableBlur = true;
            Settings.BlurMode = 1; // Acrylic
            Settings.BlurOpacity = 49;
            Settings.BackgroundOpacity = 138;
            Settings.BorderThickness = 1;

            switch (preset)
            {
                case NotificationStylePreset.MidnightBlue:
                    // Default style - deep blue
                    Settings.BlurTintColor = "00106C";
                    Settings.BackgroundColor = "071134";
                    Settings.BorderColor = "354171";
                    break;

                case NotificationStylePreset.DeepPurple:
                    // Rich purple tones
                    Settings.BlurTintColor = "4A148C";
                    Settings.BackgroundColor = "1A0033";
                    Settings.BorderColor = "631597";
                    break;

                case NotificationStylePreset.ForestGreen:
                    // Natural green
                    Settings.BlurTintColor = "1B5E20";
                    Settings.BackgroundColor = "0D2818";
                    Settings.BorderColor = "236529";
                    break;

                case NotificationStylePreset.CrimsonRed:
                    // Bold red
                    Settings.BlurTintColor = "8B0000";
                    Settings.BackgroundColor = "1A0505";
                    Settings.BorderColor = "8C1515";
                    break;

                case NotificationStylePreset.SunsetOrange:
                    // Warm orange
                    Settings.BlurTintColor = "E65100";
                    Settings.BackgroundColor = "1A0F00";
                    Settings.BorderColor = "AA4800";
                    break;

                case NotificationStylePreset.OceanTeal:
                    // Cool teal
                    Settings.BlurTintColor = "006064";
                    Settings.BackgroundColor = "001A1A";
                    Settings.BorderColor = "00757B";
                    break;

                case NotificationStylePreset.CharcoalGray:
                    // Neutral dark gray
                    Settings.BlurTintColor = "212121";
                    Settings.BackgroundColor = "0A0A0A";
                    Settings.BorderColor = "404040";
                    break;

                case NotificationStylePreset.RosePink:
                    // Soft pink
                    Settings.BlurTintColor = "880E4F";
                    Settings.BackgroundColor = "1A0510";
                    Settings.BorderColor = "810E4A";
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

                // XInput Detection (Xbox controllers)
                sb.AppendLine("XInput (Xbox controllers):");
                if (XInputWrapper.IsControllerConnected())
                {
                    sb.AppendLine("  Controller connected");
                }
                else
                {
                    sb.AppendLine("  No controller detected");
                }

                sb.AppendLine();

                // Note about Playnite handling SDL controllers
                sb.AppendLine("Note: PlayStation, Nintendo, and other controllers are");
                sb.AppendLine("detected by Playnite when 'Controller input' is enabled");
                sb.AppendLine("in Settings > General > Desktop Mode.");

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
                var dialog = new ControllerDetectedDialog(Settings, FullscreenTriggerSource.Connection, "Controller");
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to preview notification");
                PlayniteApi.Dialogs.ShowErrorMessage($"Preview failed: {ex.Message}", "Error");
            }
        });

        public RelayCommand PreviewToastCommand => new RelayCommand(() =>
        {
            try
            {
                // Sync settings before preview
                NotifierHelper.SyncSettings(Settings);
                NotifierHelper.ShowInfo("Switched to Fullscreen Mode", "ControlUp");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to preview toast");
                PlayniteApi.Dialogs.ShowErrorMessage($"Preview failed: {ex.Message}", "Error");
            }
        });
    }
}
