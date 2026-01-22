using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using ControlUp.Common;
using ControlUp.Dialogs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

namespace ControlUp
{
    public class ControlUpPlugin : GenericPlugin
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        // Prevent multiple popups
        private bool _popupShowing = false;
        private DateTime _lastPopupTime = DateTime.MinValue;
        private DateTime _dialogClosedTime = DateTime.MinValue;
        private const int POPUP_COOLDOWN_SECONDS = 5;
        private const int DIALOG_CLOSE_COOLDOWN_MS = 500; // Prevent immediate re-trigger after dialog closes

        // Hotkey tracking via SDK events
        private HashSet<ControllerInput> _pressedButtons = new HashSet<ControllerInput>();
        private volatile bool _hotkeyTriggered = false;

        // Active dialog reference for forwarding controller input
        private ControllerDetectedDialog _activeDialog = null;

        // Logging
        private FileLogger _fileLogger;

        public ControlUpSettingsViewModel Settings { get; private set; }

        public ControlUpPlugin(IPlayniteAPI playniteAPI) : base(playniteAPI)
        {
            try
            {
                var extensionPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                _fileLogger = new FileLogger(extensionPath);
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                _fileLogger.Info($"=== ControlUp v{version} Starting (SDK-based input) ===");
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
            _fileLogger?.Info($"Settings: TriggerMode={Settings.Settings.FullscreenTriggerMode}, Hotkey={Settings.Settings.EnableHotkey}, HotkeyCombo={Settings.Settings.HotkeyCombo}");

            // If already in fullscreen, no need to process
            if (currentMode == ApplicationMode.Fullscreen)
            {
                _fileLogger?.Info("Already in fullscreen mode");
                return;
            }

            // Check if controller is already connected (for startup trigger modes)
            var connectedControllers = PlayniteApi.GetConnectedControllers();
            bool controllerConnected = connectedControllers != null && connectedControllers.Any();
            _fileLogger?.Info($"Initial controller check - Connected: {controllerConnected}, Count: {connectedControllers?.Count ?? 0}");
            if (controllerConnected)
            {
                foreach (var c in connectedControllers)
                    _fileLogger?.Info($"  - {c.Name} (ID: {c.InstanceId})");
            }

            // For startup modes, trigger if controller is already connected
            var triggerMode = Settings.Settings.FullscreenTriggerMode;
            bool shouldTriggerOnStartup = controllerConnected && (
                triggerMode == FullscreenTriggerMode.AnyControllerOnStartupOnly ||
                triggerMode == FullscreenTriggerMode.AnyControllerConnectedAnytime);

            if (shouldTriggerOnStartup)
            {
                var firstControllerName = connectedControllers.FirstOrDefault()?.Name;
                _fileLogger?.Info($"Controller detected on startup ({firstControllerName}), triggering fullscreen popup");
                // Delay to let UI load
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    TriggerFullscreenSwitch(FullscreenTriggerSource.Connection, firstControllerName);
                };
                timer.Start();
            }

            // Note: Runtime connection detection is handled by OnControllerConnected callback
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            Logger.Info("ControlUp: Application stopped");
            _fileLogger?.Info("=== Application Stopped ===");
        }

        /// <summary>Called when a controller is connected.</summary>
        public override void OnControllerConnected(OnControllerConnectedArgs args)
        {
            var controller = args.Controller;
            _fileLogger?.Info($"Controller connected: {controller.Name} (ID: {controller.InstanceId})");
            Logger.Info($"ControlUp: Controller connected - {controller.Name}");

            // Don't trigger if already in fullscreen
            if (PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen)
            {
                _fileLogger?.Info("Already in fullscreen, ignoring connection");
                return;
            }

            // Check trigger mode
            var triggerMode = Settings.Settings.FullscreenTriggerMode;
            bool shouldTrigger = triggerMode == FullscreenTriggerMode.NewConnectionOnly ||
                                 triggerMode == FullscreenTriggerMode.AnyControllerConnectedAnytime;

            if (!shouldTrigger)
            {
                _fileLogger?.Info($"Trigger mode {triggerMode} doesn't respond to connections");
                return;
            }

            // Check cooldowns
            if (_popupShowing)
            {
                _fileLogger?.Info("Popup already showing, ignoring connection");
                return;
            }

            if ((DateTime.Now - _lastPopupTime).TotalSeconds < POPUP_COOLDOWN_SECONDS)
            {
                _fileLogger?.Info("In cooldown period, ignoring connection");
                return;
            }

            _fileLogger?.Info($"Triggering fullscreen switch for: {controller.Name}");
            var name = controller.Name;
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                TriggerFullscreenSwitch(FullscreenTriggerSource.Connection, name);
            }));
        }

        /// <summary>Called when a controller is disconnected.</summary>
        public override void OnControllerDisconnected(OnControllerDisconnectedArgs args)
        {
            var controller = args.Controller;
            _fileLogger?.Info($"Controller disconnected: {controller.Name} (ID: {controller.InstanceId})");
            Logger.Info($"ControlUp: Controller disconnected - {controller.Name}");
        }

        /// <summary>Handles controller button events from Playnite's SDL input system.</summary>
        public override void OnDesktopControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
        {
            // Forward to active dialog if showing
            if (_activeDialog != null && _popupShowing)
            {
                try
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        _activeDialog?.HandleControllerInput(args.Button, args.State);
                    });
                }
                catch { }
                return; // Don't process hotkeys while dialog is active
            }

            // Track button states for hotkey combo detection
            if (args.State == ControllerInputState.Pressed)
            {
                _pressedButtons.Add(args.Button);
            }
            else
            {
                _pressedButtons.Remove(args.Button);
                _hotkeyTriggered = false; // Reset trigger flag when any button is released
            }

            // Don't check hotkeys if disabled or already in fullscreen
            if (!Settings.Settings.EnableHotkey ||
                PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen ||
                _popupShowing)
            {
                return;
            }

            // Don't trigger immediately after dialog was closed (prevents B button re-trigger)
            if ((DateTime.Now - _dialogClosedTime).TotalMilliseconds < DIALOG_CLOSE_COOLDOWN_MS)
            {
                return;
            }

            // Check for hotkey combo
            if (!_hotkeyTriggered && IsHotkeyComboPressed())
            {
                _hotkeyTriggered = true;
                var controllerName = args.Controller?.Name;
                _fileLogger?.Info($"Hotkey {Settings.Settings.HotkeyCombo} detected from '{controllerName ?? "unknown"}', triggering fullscreen");

                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    TriggerFullscreenSwitch(FullscreenTriggerSource.Hotkey, controllerName);
                }));
            }
        }

        private bool IsHotkeyComboPressed()
        {
            switch (Settings.Settings.HotkeyCombo)
            {
                // Combo hotkeys
                case ControllerHotkey.StartPlusRB:
                    return _pressedButtons.Contains(ControllerInput.Start) &&
                           _pressedButtons.Contains(ControllerInput.RightShoulder);
                case ControllerHotkey.StartPlusLB:
                    return _pressedButtons.Contains(ControllerInput.Start) &&
                           _pressedButtons.Contains(ControllerInput.LeftShoulder);
                case ControllerHotkey.BackPlusStart:
                    return _pressedButtons.Contains(ControllerInput.Back) &&
                           _pressedButtons.Contains(ControllerInput.Start);
                case ControllerHotkey.BackPlusRB:
                    return _pressedButtons.Contains(ControllerInput.Back) &&
                           _pressedButtons.Contains(ControllerInput.RightShoulder);
                case ControllerHotkey.BackPlusLB:
                    return _pressedButtons.Contains(ControllerInput.Back) &&
                           _pressedButtons.Contains(ControllerInput.LeftShoulder);

                // Single button hotkeys
                case ControllerHotkey.GuideButton:
                    return _pressedButtons.Contains(ControllerInput.Guide);
                case ControllerHotkey.StartButton:
                    return _pressedButtons.Contains(ControllerInput.Start);
                case ControllerHotkey.BackButton:
                    return _pressedButtons.Contains(ControllerInput.Back);

                default:
                    return false;
            }
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return Settings;
        }

        public override System.Windows.Controls.UserControl GetSettingsView(bool firstRunView)
        {
            return new ControlUpSettingsView(Settings);
        }

        private void TriggerFullscreenSwitch(FullscreenTriggerSource source, string controllerName = null)
        {
            _popupShowing = true;
            _lastPopupTime = DateTime.Now;
            _fileLogger?.Info($"TriggerFullscreenSwitch called with source: {source}, controller: {controllerName ?? "unknown"}");

            try
            {
                bool skipPopup = source == FullscreenTriggerSource.Connection
                    ? Settings.Settings.SkipPopupOnConnection
                    : Settings.Settings.SkipPopupOnHotkey;

                if (skipPopup)
                {
                    _fileLogger?.Info($"Skipping popup (source: {source}), switching directly to fullscreen");
                    SwitchToFullscreen();
                    _popupShowing = false;
                }
                else
                {
                    _activeDialog = new ControllerDetectedDialog(Settings.Settings, source, controllerName);
                    var result = _activeDialog.ShowDialog();

                    // Clear pressed buttons and set cooldown to prevent immediate re-trigger
                    _pressedButtons.Clear();
                    _dialogClosedTime = DateTime.Now;
                    _hotkeyTriggered = false;

                    _fileLogger?.Info($"Dialog closed with result: {result}, UserSelectedYes: {_activeDialog?.UserSelectedYes}");

                    if (result == true && _activeDialog.UserSelectedYes)
                    {
                        _fileLogger?.Info("User selected Yes - switching to fullscreen now");
                        SwitchToFullscreen();
                    }
                    else
                    {
                        _fileLogger?.Info("User selected Cancel or dialog timed out");
                    }

                    _activeDialog = null;
                    _popupShowing = false;
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"Error triggering fullscreen: {ex.Message}");
                _fileLogger?.Error($"Stack trace: {ex.StackTrace}");
                Logger.Error(ex, "ControlUp: Error triggering fullscreen switch");
                _popupShowing = false;
                _activeDialog = null;
            }
        }

        private void SwitchToFullscreen()
        {
            string fullscreenExe = Path.Combine(PlayniteApi.Paths.ApplicationPath, "Playnite.FullscreenApp.exe");
            _fileLogger?.Info($"Launching: {fullscreenExe}");

            if (File.Exists(fullscreenExe))
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fullscreenExe,
                    UseShellExecute = false,
                    WorkingDirectory = PlayniteApi.Paths.ApplicationPath
                };

                System.Diagnostics.Process.Start(startInfo);
                _fileLogger?.Info("Fullscreen app launched");

                Application.Current.Shutdown();
            }
            else
            {
                _fileLogger?.Error($"Fullscreen app not found: {fullscreenExe}");
            }
        }
    }
}
