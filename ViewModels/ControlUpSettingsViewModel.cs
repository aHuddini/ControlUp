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

                // Primary: XInput detection
                var xinputInfo = XInputWrapper.GetControllerInfo();
                sb.AppendLine("XInput API (Primary):");
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

                // Secondary: Windows.Gaming.Input
                sb.AppendLine("Windows.Gaming.Input API (Secondary):");
                if (!GamingInputWrapper.IsAvailable)
                {
                    sb.AppendLine("  • API not available on this system");
                }
                else
                {
                    int count = GamingInputWrapper.GetControllerCount();
                    if (count > 0)
                    {
                        var name = GamingInputWrapper.GetControllerName();
                        sb.AppendLine($"  ✓ {name ?? "Game Controller"} ({count} detected)");
                    }
                    else
                    {
                        sb.AppendLine("  • No controllers detected via this API");
                    }
                }
                sb.AppendLine();

                // Fallback: HID enumeration
                sb.AppendLine("HID Enumeration (Fallback):");
                var hidControllers = DirectInputWrapper.GetConnectedControllerNames();
                if (hidControllers.Any())
                {
                    foreach (var name in hidControllers.Take(5)) // Limit to 5
                    {
                        sb.AppendLine($"  • {name}");
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
                sb.AppendLine();

                // Diagnostic: Show Sony/PlayStation device paths
                sb.AppendLine("PlayStation Device Paths (Diagnostic):");
                var allPaths = DirectInputWrapper.GetAllHidDevicePaths();
                var sonyPaths = allPaths.Where(p => p.ToLowerInvariant().Contains("054c") ||
                                                     p.ToLowerInvariant().Contains("sony") ||
                                                     p.ToLowerInvariant().Contains("dualsense") ||
                                                     p.ToLowerInvariant().Contains("dualshock")).ToList();
                if (sonyPaths.Any())
                {
                    foreach (var path in sonyPaths.Take(3))
                    {
                        // Show shortened path for readability
                        var shortPath = path.Length > 80 ? path.Substring(0, 80) + "..." : path;
                        sb.AppendLine($"  • {shortPath}");
                    }
                }
                else
                {
                    sb.AppendLine("  • No Sony/PlayStation HID devices found");
                    sb.AppendLine($"  (Total HID devices: {allPaths.Count})");
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
