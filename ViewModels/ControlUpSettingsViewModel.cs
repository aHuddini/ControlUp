using Playnite.SDK;
using Playnite.SDK.Data;
using ControlUp.Dialogs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

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

                // Use Playnite's SDK to get connected controllers
                var controllers = PlayniteApi.GetConnectedControllers();

                if (controllers != null && controllers.Count > 0)
                {
                    controllerInfo.AppendLine($"Detected {controllers.Count} Controller(s):");
                    foreach (var controller in controllers)
                    {
                        controllerInfo.AppendLine($"  - {controller.Name} (ID: {controller.InstanceId})");
                    }
                    controllerInfo.AppendLine();
                }
                else
                {
                    controllerInfo.AppendLine("No controllers detected via Playnite SDK.");
                    controllerInfo.AppendLine();
                }

                controllerInfo.AppendLine("Note: Controller detection uses Playnite's SDL support.");
                controllerInfo.AppendLine("Enable 'Controller input' in Playnite Desktop settings.");

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
