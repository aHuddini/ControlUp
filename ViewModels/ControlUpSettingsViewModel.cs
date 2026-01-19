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
using System.Threading.Tasks;
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

        public ControlUpSettingsViewModel(ControlUpPlugin plugin, IPlayniteAPI playniteApi)
        {
            Plugin = plugin;
            PlayniteApi = playniteApi;

            var savedSettings = Plugin.LoadPluginSettings<ControlUpSettings>();
            if (savedSettings != null)
            {
                Settings = savedSettings;
            }
            else
            {
                Settings = new ControlUpSettings();
            }
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
                var controllerInfo = new StringBuilder();
                controllerInfo.AppendLine("Scanning for connected controllers...\n");

                // Check XInput controllers
                var xinputControllers = GetXInputControllerInfo();
                controllerInfo.AppendLine("XInput (Xbox USB Controllers):");
                if (xinputControllers.Any())
                {
                    foreach (var controller in xinputControllers)
                    {
                        controllerInfo.AppendLine($"  • {controller}");
                    }
                }
                else
                {
                    controllerInfo.AppendLine("  • No XInput controllers detected");
                }
                controllerInfo.AppendLine();

                // Check Windows.Gaming.Input controllers
                var gamingInputControllers = GetGamingInputControllerInfo();
                controllerInfo.AppendLine("Windows.Gaming.Input (Xbox/Bluetooth Controllers):");
                if (gamingInputControllers.Any() && !gamingInputControllers.Contains("No controllers detected"))
                {
                    foreach (var controller in gamingInputControllers)
                    {
                        controllerInfo.AppendLine($"  • {controller}");
                    }
                }
                else
                {
                    // Try enhanced Xbox detection if standard detection fails
                    if (GamingInputWrapper.IsXboxControllerConnected())
                    {
                        controllerInfo.AppendLine("  • Xbox Controller (Enhanced Detection)");
                        controllerInfo.AppendLine("    Note: Controller detected via alternative methods");
                    }
                    else
                    {
                        controllerInfo.AppendLine("  • No Windows.Gaming.Input controllers detected");
                        controllerInfo.AppendLine("    Note: Windows.Gaming.Input may not be available or controllers not paired");
                    }
                }
                controllerInfo.AppendLine();

                // Check DirectInput HID controllers
                var directInputControllers = GetDirectInputControllerInfo();
                controllerInfo.AppendLine("DirectInput HID Controllers:");
                if (directInputControllers.Any())
                {
                    foreach (var controller in directInputControllers)
                    {
                        controllerInfo.AppendLine($"  • {controller}");
                    }
                }
                else
                {
                    controllerInfo.AppendLine("  • No DirectInput controllers detected");
                }
                controllerInfo.AppendLine();

                // Check Raw Input controllers
                var rawInputControllers = GetRawInputControllerInfo();
                controllerInfo.AppendLine("Raw Input HID Controllers:");
                if (rawInputControllers.Any())
                {
                    foreach (var controller in rawInputControllers)
                    {
                        controllerInfo.AppendLine($"  • {controller}");
                    }
                }
                else
                {
                    controllerInfo.AppendLine("  • No Raw Input controllers detected");
                }

                DetectedControllersText = controllerInfo.ToString();
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
                // Get the extension directory
                string extensionPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                if (Directory.Exists(extensionPath))
                {
                    // Try to open the folder and select the log file if it exists
                    string logFile = Path.Combine(extensionPath, "ControlUp.log");
                    if (File.Exists(logFile))
                    {
                        // Open folder and select the log file
                        Process.Start("explorer.exe", $"/select,\"{logFile}\"");
                    }
                    else
                    {
                        // Just open the folder
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
                PlayniteApi.Dialogs.ShowErrorMessage($"Failed to open extension folder: {ex.Message}", "Error");
            }
        });

        private List<string> GetXInputControllerInfo()
        {
            var controllers = new List<string>();
            try
            {
                // Check all 4 possible XInput controller slots
                for (uint i = 0; i < 4; i++)
                {
                    if (XInputWrapper.IsControllerConnectedToSlot(i))
                    {
                        controllers.Add($"Controller {i + 1} (USB Xbox Controller)");
                    }
                }
            }
            catch
            {
                controllers.Add("Error checking XInput controllers");
            }
            return controllers;
        }

        private List<string> GetGamingInputControllerInfo()
        {
            try
            {
                var controllers = GamingInputWrapper.GetConnectedControllerInfo();

                // If Windows.Gaming.Input shows no controllers but we suspect Xbox controllers are connected,
                // try the enhanced detection
                if (controllers.Contains("No controllers detected") || controllers.Contains("Windows.Gaming.Input not available"))
                {
                    // Try enhanced Xbox detection
                    if (GamingInputWrapper.IsXboxControllerConnected())
                    {
                        return new List<string> { "Xbox Controller (Enhanced Detection)" };
                    }
                }

                return controllers;
            }
            catch
            {
                return new List<string> { "Error checking Windows.Gaming.Input controllers" };
            }
        }

        private List<string> GetDirectInputControllerInfo()
        {
            var controllers = new List<string>();
            try
            {
                // Get the list of detected controllers from DirectInputWrapper
                var directInputControllers = DirectInputWrapper.GetConnectedControllerNames();
                controllers.AddRange(directInputControllers);
            }
            catch
            {
                controllers.Add("Error checking DirectInput controllers");
            }
            return controllers;
        }

        private List<string> GetRawInputControllerInfo()
        {
            var controllers = new List<string>();
            try
            {
                if (RawInputWrapper.IsControllerConnected())
                {
                    controllers.Add("HID Game Controller (Raw Input)");
                }
            }
            catch
            {
                controllers.Add("Error checking Raw Input controllers");
            }
            return controllers;
        }

        public RelayCommand PreviewNotificationCommand => new RelayCommand(() =>
        {
            try
            {
                var dialog = new ControllerDetectedDialog(Settings);
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to preview notification");
                PlayniteApi.Dialogs.ShowErrorMessage($"Failed to preview notification: {ex.Message}", "Error");
            }
        });
    }
}