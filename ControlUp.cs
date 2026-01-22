using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using ControlUp.Common;
using ControlUp.Dialogs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

namespace ControlUp
{
    public class ControlUpPlugin : GenericPlugin
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        // Connection monitoring (minimal timer for detecting new connections)
        private DispatcherTimer _connectionTimer;
        private bool _controllerWasConnected = false;

        // Prevent multiple popups
        private bool _popupShowing = false;
        private DateTime _lastPopupTime = DateTime.MinValue;
        private DateTime _dialogClosedTime = DateTime.MinValue;
        private const int POPUP_COOLDOWN_SECONDS = 30;
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

            // Check initial controller state (use HID detection for broader compatibility)
            bool xinputConnected = XInputWrapper.IsControllerConnected();
            bool hidConnected = HidControllerDetector.IsAnyControllerConnected();
            _controllerWasConnected = xinputConnected || hidConnected;
            _fileLogger?.Info($"Initial controller check - XInput: {xinputConnected}, HID: {hidConnected}, Any: {_controllerWasConnected}");

            // If already in fullscreen, no need to monitor
            if (currentMode == ApplicationMode.Fullscreen)
            {
                _fileLogger?.Info("Already in fullscreen mode, monitoring disabled");
                return;
            }

            // For startup modes, trigger if controller is already connected
            var triggerMode = Settings.Settings.FullscreenTriggerMode;
            _fileLogger?.Info($"Checking startup trigger: Mode={triggerMode}, ControllerConnected={_controllerWasConnected}");

            bool shouldTriggerOnStartup = _controllerWasConnected && (
                triggerMode == FullscreenTriggerMode.AnyControllerOnStartupOnly ||
                triggerMode == FullscreenTriggerMode.AnyControllerConnectedAnytime);

            if (shouldTriggerOnStartup)
            {
                _fileLogger?.Info("Controller detected on startup, triggering fullscreen popup");
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };  // Give UI time to load
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    TriggerFullscreenSwitch(FullscreenTriggerSource.Connection);
                };
                timer.Start();
            }
            else
            {
                _fileLogger?.Info($"Not triggering on startup: shouldTrigger={shouldTriggerOnStartup}");
            }

            // Start connection monitoring for runtime detection modes
            StartConnectionMonitoring();
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            Logger.Info("ControlUp: Application stopped");
            _fileLogger?.Info("=== Application Stopped ===");
            StopConnectionMonitoring();
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
                _fileLogger?.Info($"Hotkey {Settings.Settings.HotkeyCombo} detected via SDK, triggering fullscreen");

                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    TriggerFullscreenSwitch(FullscreenTriggerSource.Hotkey);
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

        private void StartConnectionMonitoring()
        {
            var triggerMode = Settings.Settings.FullscreenTriggerMode;
            // Need runtime monitoring for "Anytime" and "NewConnectionOnly" modes
            // (startup-only doesn't need continuous monitoring)
            bool needsConnectionMonitoring = triggerMode == FullscreenTriggerMode.AnyControllerConnectedAnytime ||
                                             triggerMode == FullscreenTriggerMode.NewConnectionOnly;

            if (!needsConnectionMonitoring)
            {
                _fileLogger?.Info("Connection monitoring not needed for current trigger mode");
                return;
            }

            _connectionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _connectionTimer.Tick += OnConnectionTimerTick;
            _connectionTimer.Start();
            _fileLogger?.Info("Started connection monitoring (500ms interval)");
        }

        private void StopConnectionMonitoring()
        {
            if (_connectionTimer != null)
            {
                _connectionTimer.Stop();
                _connectionTimer.Tick -= OnConnectionTimerTick;
                _connectionTimer = null;
            }
            _fileLogger?.Info("Stopped connection monitoring");
        }

        private void OnConnectionTimerTick(object sender, EventArgs e)
        {
            // Don't monitor if already in fullscreen
            if (PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen)
                return;

            // Don't check if popup is showing or in cooldown
            if (_popupShowing)
                return;

            if ((DateTime.Now - _lastPopupTime).TotalSeconds < POPUP_COOLDOWN_SECONDS)
                return;

            // Check both XInput and HID for broader controller support (PS5, Switch, etc.)
            bool controllerNowConnected = XInputWrapper.IsControllerConnected() || HidControllerDetector.IsAnyControllerConnected();

            // Detect NEW connection (was disconnected, now connected)
            if (controllerNowConnected && !_controllerWasConnected)
            {
                _fileLogger?.Info("New controller connection detected, triggering fullscreen");
                TriggerFullscreenSwitch(FullscreenTriggerSource.Connection);
            }

            _controllerWasConnected = controllerNowConnected;
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return Settings;
        }

        public override System.Windows.Controls.UserControl GetSettingsView(bool firstRunView)
        {
            return new ControlUpSettingsView(Settings);
        }

        private void TriggerFullscreenSwitch(FullscreenTriggerSource source)
        {
            _popupShowing = true;
            _lastPopupTime = DateTime.Now;

            try
            {
                bool skipPopup = source == FullscreenTriggerSource.Connection
                    ? Settings.Settings.SkipPopupOnConnection
                    : Settings.Settings.SkipPopupOnHotkey;

                if (skipPopup)
                {
                    _fileLogger?.Info($"Skipping popup (source: {source}), switching directly to fullscreen");
                    Application.Current.Dispatcher.Invoke(() => SwitchToFullscreen());
                }
                else
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        _activeDialog = new ControllerDetectedDialog(Settings.Settings, source);
                        var result = _activeDialog.ShowDialog();

                        // Clear pressed buttons and set cooldown to prevent immediate re-trigger
                        _pressedButtons.Clear();
                        _dialogClosedTime = DateTime.Now;
                        _hotkeyTriggered = false;

                        if (result == true && _activeDialog.UserSelectedYes)
                        {
                            _fileLogger?.Info("User selected Yes - switching to fullscreen");
                            // Delay the switch slightly to ensure dialog is fully closed
                            var switchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                            switchTimer.Tick += (ts, te) =>
                            {
                                switchTimer.Stop();
                                SwitchToFullscreen();
                            };
                            switchTimer.Start();
                        }
                        else
                        {
                            _fileLogger?.Info("User selected Cancel or dialog timed out");
                        }

                        _activeDialog = null;
                    });
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"Error triggering fullscreen: {ex.Message}");
                Logger.Error(ex, "ControlUp: Error triggering fullscreen switch");
            }
            finally
            {
                _popupShowing = false;
                _activeDialog = null;
            }
        }

        private void SwitchToFullscreen()
        {
            try
            {
                string fullscreenExe = Path.Combine(PlayniteApi.Paths.ApplicationPath, "Playnite.FullscreenApp.exe");
                _fileLogger?.Info($"Launching: {fullscreenExe}");

                if (File.Exists(fullscreenExe))
                {
                    System.Diagnostics.Process.Start(fullscreenExe);
                }
                else
                {
                    _fileLogger?.Error($"Fullscreen app not found: {fullscreenExe}");
                    Logger.Error($"ControlUp: Could not find Playnite.FullscreenApp.exe");
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"Error switching to fullscreen: {ex.Message}");
                Logger.Error(ex, "ControlUp: Error switching to fullscreen");
            }
        }
    }
}
