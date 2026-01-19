using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using ControlUp.Common;
using ControlUp.Dialogs;
using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

namespace ControlUp
{
    /// <summary>
    /// ControlUp - Shows a popup when a controller is connected, allowing user to switch to fullscreen mode.
    /// </summary>
    public class ControlUpPlugin : GenericPlugin
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        // Monitoring timer
        private DispatcherTimer _monitoringTimer;

        // Controller state tracking
        private bool _xinputConnected = false;
        private bool _gamingInputConnected = false;

        // Prevent multiple popups
        private bool _popupShowing = false;
        private DateTime _lastPopupTime = DateTime.MinValue;
        private const int POPUP_COOLDOWN_SECONDS = 30;

        // Components
        private FileLogger _fileLogger;

        public ControlUpSettingsViewModel Settings { get; private set; }

        public ControlUpPlugin(IPlayniteAPI playniteAPI) : base(playniteAPI)
        {
            try
            {
                var extensionPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                _fileLogger = new FileLogger(extensionPath);
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                _fileLogger.Info($"=== ControlUp v{version} Starting ===");
            }
            catch
            {
                // File logger initialization failed - continue without it
            }

            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };

            Settings = new ControlUpSettingsViewModel(this, PlayniteApi);

            AddSettingsSupport(new AddSettingsSupportArgs
            {
                SourceName = "ControlUp",
                SettingsRoot = $"{nameof(Settings)}.{nameof(Settings.Settings)}"
            });
        }

        public override Guid Id => Guid.Parse("8d646e1b-c919-49d7-be40-5ef9960064bc");

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            var currentMode = PlayniteApi.ApplicationInfo.Mode;
            Logger.Info($"ControlUp: Application started - Mode: {currentMode}");
            _fileLogger?.Info($"=== Application Started - Mode: {currentMode} ===");
            _fileLogger?.Info($"Settings: TriggerMode={Settings.Settings.FullscreenTriggerMode}");

            // Check initial controller state
            _xinputConnected = XInputWrapper.IsControllerConnected();
            _gamingInputConnected = GamingInputWrapper.IsControllerConnected();

            _fileLogger?.Info($"Initial state - XInput: {_xinputConnected}, GamingInput: {_gamingInputConnected}");

            // If already in fullscreen, no need to monitor or show popup
            if (currentMode == ApplicationMode.Fullscreen)
            {
                _fileLogger?.Info("Already in fullscreen mode, monitoring disabled");
                return;
            }

            // If disabled, don't do anything
            if (Settings.Settings.FullscreenTriggerMode == FullscreenTriggerMode.Disabled)
            {
                _fileLogger?.Info("Extension is disabled, monitoring not started");
                return;
            }

            // For "OnStartup" modes, show popup immediately if controller is already connected
            var triggerMode = Settings.Settings.FullscreenTriggerMode;
            if (triggerMode == FullscreenTriggerMode.UsbControllerOnStartup && _xinputConnected)
            {
                _fileLogger?.Info("USB controller detected on startup, showing popup");
                // Delay slightly to let Playnite fully load
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    ShowControllerDetectedPopup("USB/XInput (startup)");
                };
                timer.Start();
                return; // Don't start continuous monitoring for startup modes
            }
            else if (triggerMode == FullscreenTriggerMode.BluetoothControllerOnStartup && _gamingInputConnected)
            {
                _fileLogger?.Info("Bluetooth controller detected on startup, showing popup");
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    ShowControllerDetectedPopup("Bluetooth (startup)");
                };
                timer.Start();
                return; // Don't start continuous monitoring for startup modes
            }

            // For non-startup modes, start continuous monitoring for new connections
            if (triggerMode != FullscreenTriggerMode.UsbControllerOnStartup &&
                triggerMode != FullscreenTriggerMode.BluetoothControllerOnStartup)
            {
                StartMonitoring();
            }
            else
            {
                _fileLogger?.Info("Startup mode but no controller detected at startup");
            }
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            Logger.Info("ControlUp: Application stopped");
            _fileLogger?.Info("=== Application Stopped ===");
            StopMonitoring();
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return Settings;
        }

        public override System.Windows.Controls.UserControl GetSettingsView(bool firstRunView)
        {
            return new ControlUpSettingsView(Settings);
        }

        private void StartMonitoring()
        {
            _monitoringTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _monitoringTimer.Tick += OnMonitoringTimerTick;
            _monitoringTimer.Start();

            Logger.Info("ControlUp: Started controller monitoring");
            _fileLogger?.Info("Started controller monitoring (500ms interval)");
        }

        private void StopMonitoring()
        {
            if (_monitoringTimer != null)
            {
                _monitoringTimer.Stop();
                _monitoringTimer.Tick -= OnMonitoringTimerTick;
                _monitoringTimer = null;
            }

            Logger.Info("ControlUp: Stopped monitoring");
            _fileLogger?.Info("Stopped monitoring");
        }

        private void OnMonitoringTimerTick(object sender, EventArgs e)
        {
            // Don't check if popup is showing or we're in cooldown
            if (_popupShowing)
                return;

            if ((DateTime.Now - _lastPopupTime).TotalSeconds < POPUP_COOLDOWN_SECONDS)
                return;

            // Don't monitor if already in fullscreen
            if (PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen)
                return;

            CheckControllerState();
        }

        private void CheckControllerState()
        {
            var triggerMode = Settings.Settings.FullscreenTriggerMode;
            if (triggerMode == FullscreenTriggerMode.Disabled)
                return;

            bool xinputNow = XInputWrapper.IsControllerConnected();
            bool gamingInputNow = GamingInputWrapper.IsControllerConnected();

            bool shouldShowPopup = false;
            string controllerType = "";

            // Check for NEW connections based on trigger mode
            switch (triggerMode)
            {
                case FullscreenTriggerMode.UsbControllerConnected:
                case FullscreenTriggerMode.UsbControllerOnStartup:
                    // USB/XInput controller newly connected
                    if (xinputNow && !_xinputConnected)
                    {
                        shouldShowPopup = true;
                        controllerType = "USB/XInput";
                    }
                    break;

                case FullscreenTriggerMode.BluetoothControllerConnected:
                case FullscreenTriggerMode.BluetoothControllerOnStartup:
                    // Bluetooth/GamingInput controller newly connected
                    if (gamingInputNow && !_gamingInputConnected)
                    {
                        shouldShowPopup = true;
                        controllerType = "Bluetooth";
                    }
                    break;

                case FullscreenTriggerMode.AnyControllerConnected:
                    // Any controller newly connected
                    if ((xinputNow && !_xinputConnected) || (gamingInputNow && !_gamingInputConnected))
                    {
                        shouldShowPopup = true;
                        controllerType = xinputNow ? "USB/XInput" : "Bluetooth";
                    }
                    break;
            }

            // Update tracked state
            _xinputConnected = xinputNow;
            _gamingInputConnected = gamingInputNow;

            // Show popup if needed
            if (shouldShowPopup)
            {
                _fileLogger?.Info($"New {controllerType} controller detected, showing popup");
                ShowControllerDetectedPopup(controllerType);
            }
        }

        private void ShowControllerDetectedPopup(string controllerType)
        {
            _popupShowing = true;
            _lastPopupTime = DateTime.Now;

            try
            {
                // Must run on UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var dialog = new ControllerDetectedDialog(Settings.Settings);
                    var result = dialog.ShowDialog();

                    if (result == true && dialog.UserSelectedYes)
                    {
                        _fileLogger?.Info("User selected Yes - switching to fullscreen");
                        SwitchToFullscreen();
                    }
                    else
                    {
                        _fileLogger?.Info("User selected Cancel or dialog timed out");
                    }
                });
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"Error showing popup: {ex.Message}");
                Logger.Error(ex, "ControlUp: Error showing controller detected popup");
            }
            finally
            {
                _popupShowing = false;
            }
        }

        private void SwitchToFullscreen()
        {
            try
            {
                _fileLogger?.Info("Switching to fullscreen mode");

                // Try reflection method first
                var mainViewType = PlayniteApi.MainView.GetType();
                var switchMethod = mainViewType.GetMethod("SwitchToFullscreenMode",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

                if (switchMethod != null)
                {
                    _fileLogger?.Info("Using SwitchToFullscreenMode via reflection");
                    switchMethod.Invoke(PlayniteApi.MainView, null);
                }
                else
                {
                    // Fallback: Send F11 key
                    _fileLogger?.Info("SwitchToFullscreenMode not found, sending F11");
                    SendF11Key();
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"Error switching to fullscreen: {ex.Message}");
                Logger.Error(ex, "ControlUp: Error switching to fullscreen");
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const byte VK_F11 = 0x7A;
        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private void SendF11Key()
        {
            keybd_event(VK_F11, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
            System.Threading.Thread.Sleep(50);
            keybd_event(VK_F11, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
    }
}
