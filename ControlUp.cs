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

        // Popup state
        private volatile bool _popupShowing = false;
        private DateTime _dialogClosedTime = DateTime.MinValue;
        private const int DIALOG_CLOSE_COOLDOWN_MS = 500;

        // Hotkey tracking via SDK events
        private HashSet<ControllerInput> _pressedButtons = new HashSet<ControllerInput>();
        private volatile bool _hotkeyTriggered = false;
        private DateTime _hotkeyPressStartTime = DateTime.MinValue;
        private bool _hotkeyLongPressTriggered = false;

        // Active dialog reference for forwarding controller input
        private ControllerDetectedDialog _activeDialog = null;

        // Logging
        private FileLogger _fileLogger;

        public ControlUpSettingsViewModel Settings { get; private set; }

        // Track settings for change detection
        private FullscreenTriggerMode _lastTriggerMode;
        private bool _lastEnableHotkey;

        public ControlUpPlugin(IPlayniteAPI playniteAPI) : base(playniteAPI)
        {
            try
            {
                var extensionPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                _fileLogger = new FileLogger(extensionPath);
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                _fileLogger.Info($"=== ControlUp v{version} Starting (SDK-based input) ===");

                // Share logger with dialog
                ControllerDetectedDialog.Logger = _fileLogger;
            }
            catch
            {
                // Continue without file logger
            }

            Properties = new GenericPluginProperties { HasSettings = true };
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
            var triggerMode = Settings.Settings.FullscreenTriggerMode;

            _fileLogger?.Info($"=== Application Started - Mode: {currentMode} ===");
            _fileLogger?.Info($"Settings: TriggerMode={triggerMode}, Hotkey={Settings.Settings.EnableHotkey}, HotkeyCombo={Settings.Settings.HotkeyCombo}");

            if (currentMode == ApplicationMode.Fullscreen)
            {
                _fileLogger?.Info("Already in fullscreen mode, monitoring disabled");
                return;
            }

            // Check initial controller state using XInput (quick connection check)
            _controllerWasConnected = XInputWrapper.IsControllerConnected();
            _fileLogger?.Info($"Initial controller state: Connected={_controllerWasConnected}");

            // Store current settings for change detection
            _lastTriggerMode = triggerMode;
            _lastEnableHotkey = Settings.Settings.EnableHotkey;

            // Check if this is a startup-only mode
            bool isStartupMode = triggerMode == FullscreenTriggerMode.StartupOnly;

            if (isStartupMode)
            {
                // Startup modes: only trigger at startup
                if (_controllerWasConnected)
                {
                    _fileLogger?.Info("Startup mode: Controller detected, triggering fullscreen");
                    DelayedTrigger(500, () => TriggerFullscreenSwitch(FullscreenTriggerSource.Connection));
                }
                return;
            }

            // Runtime modes (NewConnectionOnly, AnyControllerAnytime):
            if (_controllerWasConnected && triggerMode == FullscreenTriggerMode.AnyControllerAnytime)
            {
                _fileLogger?.Info("AnyControllerAnytime: Controller already connected at startup, triggering fullscreen");
                DelayedTrigger(500, () => TriggerFullscreenSwitch(FullscreenTriggerSource.Connection));
            }

            // Start connection monitoring for runtime modes
            StartConnectionMonitoring();
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            _fileLogger?.Info("=== Application Stopped ===");
            StopConnectionMonitoring();
        }

        public override ISettings GetSettings(bool firstRunSettings) => Settings;

        public override System.Windows.Controls.UserControl GetSettingsView(bool firstRunView)
        {
            return new ControlUpSettingsView(Settings);
        }

        /// <summary>
        /// SDK callback - receives all controller button events from Playnite's SDL input system.
        /// This replaces our custom polling thread.
        /// </summary>
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
                // Reset tracking when any button is released
                _hotkeyTriggered = false;
                _hotkeyPressStartTime = DateTime.MinValue;
                _hotkeyLongPressTriggered = false;
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
            if (IsHotkeyComboPressed())
            {
                bool requireLongPress = Settings.Settings.RequireLongPress;
                int longPressDelayMs = Settings.Settings.LongPressDelayMs;

                if (requireLongPress)
                {
                    // Long press mode - start tracking
                    if (_hotkeyPressStartTime == DateTime.MinValue)
                    {
                        _hotkeyPressStartTime = DateTime.Now;
                        _fileLogger?.Debug($"Hotkey combo held - starting long press timer ({longPressDelayMs}ms required)");
                    }
                    else if (!_hotkeyLongPressTriggered)
                    {
                        double heldMs = (DateTime.Now - _hotkeyPressStartTime).TotalMilliseconds;
                        if (heldMs >= longPressDelayMs)
                        {
                            _hotkeyLongPressTriggered = true;
                            _fileLogger?.Info($"Hotkey {Settings.Settings.HotkeyCombo} long-pressed for {heldMs:F0}ms");
                            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                TriggerFullscreenSwitch(FullscreenTriggerSource.Hotkey);
                            }));
                        }
                    }
                }
                else
                {
                    // Instant tap mode
                    if (!_hotkeyTriggered)
                    {
                        _hotkeyTriggered = true;
                        _fileLogger?.Info($"Hotkey {Settings.Settings.HotkeyCombo} detected via SDK");
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            TriggerFullscreenSwitch(FullscreenTriggerSource.Hotkey);
                        }));
                    }
                }
            }
        }

        private bool IsHotkeyComboPressed()
        {
            switch (Settings.Settings.HotkeyCombo)
            {
                // Start combos
                case ControllerHotkey.StartPlusRB:
                    return _pressedButtons.Contains(ControllerInput.Start) &&
                           _pressedButtons.Contains(ControllerInput.RightShoulder);
                case ControllerHotkey.StartPlusLB:
                    return _pressedButtons.Contains(ControllerInput.Start) &&
                           _pressedButtons.Contains(ControllerInput.LeftShoulder);
                case ControllerHotkey.StartPlusBack:
                    return _pressedButtons.Contains(ControllerInput.Start) &&
                           _pressedButtons.Contains(ControllerInput.Back);

                // Back combos
                case ControllerHotkey.BackPlusStart:
                    return _pressedButtons.Contains(ControllerInput.Back) &&
                           _pressedButtons.Contains(ControllerInput.Start);
                case ControllerHotkey.BackPlusRB:
                    return _pressedButtons.Contains(ControllerInput.Back) &&
                           _pressedButtons.Contains(ControllerInput.RightShoulder);
                case ControllerHotkey.BackPlusLB:
                    return _pressedButtons.Contains(ControllerInput.Back) &&
                           _pressedButtons.Contains(ControllerInput.LeftShoulder);

                // Guide combos
                case ControllerHotkey.GuidePlusStart:
                    return _pressedButtons.Contains(ControllerInput.Guide) &&
                           _pressedButtons.Contains(ControllerInput.Start);
                case ControllerHotkey.GuidePlusBack:
                    return _pressedButtons.Contains(ControllerInput.Guide) &&
                           _pressedButtons.Contains(ControllerInput.Back);
                case ControllerHotkey.GuidePlusRB:
                    return _pressedButtons.Contains(ControllerInput.Guide) &&
                           _pressedButtons.Contains(ControllerInput.RightShoulder);
                case ControllerHotkey.GuidePlusLB:
                    return _pressedButtons.Contains(ControllerInput.Guide) &&
                           _pressedButtons.Contains(ControllerInput.LeftShoulder);

                // Shoulder combos
                case ControllerHotkey.LBPlusRB:
                    return _pressedButtons.Contains(ControllerInput.LeftShoulder) &&
                           _pressedButtons.Contains(ControllerInput.RightShoulder);
                case ControllerHotkey.LBPlusRBPlusStart:
                    return _pressedButtons.Contains(ControllerInput.LeftShoulder) &&
                           _pressedButtons.Contains(ControllerInput.RightShoulder) &&
                           _pressedButtons.Contains(ControllerInput.Start);
                case ControllerHotkey.LBPlusRBPlusBack:
                    return _pressedButtons.Contains(ControllerInput.LeftShoulder) &&
                           _pressedButtons.Contains(ControllerInput.RightShoulder) &&
                           _pressedButtons.Contains(ControllerInput.Back);

                // Single button hotkeys
                case ControllerHotkey.Guide:
                    return _pressedButtons.Contains(ControllerInput.Guide);
                case ControllerHotkey.Back:
                    return _pressedButtons.Contains(ControllerInput.Back);
                case ControllerHotkey.Start:
                    return _pressedButtons.Contains(ControllerInput.Start);

                default:
                    return false;
            }
        }

        private void StartConnectionMonitoring()
        {
            var triggerMode = Settings.Settings.FullscreenTriggerMode;
            bool needsConnectionMonitoring = triggerMode == FullscreenTriggerMode.NewConnectionOnly ||
                                              triggerMode == FullscreenTriggerMode.AnyControllerAnytime;

            if (!needsConnectionMonitoring)
            {
                _fileLogger?.Info("Connection monitoring not needed for current trigger mode");
                return;
            }

            _connectionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _connectionTimer.Tick += OnConnectionTimerTick;
            _connectionTimer.Start();
            _fileLogger?.Info("Started connection monitoring (1000ms interval)");
        }

        private void StopConnectionMonitoring()
        {
            if (_connectionTimer != null)
            {
                _connectionTimer.Stop();
                _connectionTimer.Tick -= OnConnectionTimerTick;
                _connectionTimer = null;
            }
        }

        private void OnConnectionTimerTick(object sender, EventArgs e)
        {
            if (PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen || _popupShowing)
                return;

            bool controllerNowConnected = XInputWrapper.IsControllerConnected();

            // Detect NEW connection
            if (controllerNowConnected && !_controllerWasConnected)
            {
                _fileLogger?.Info("New controller connection detected");
                _controllerWasConnected = true;
                TriggerFullscreenSwitch(FullscreenTriggerSource.Connection);
            }
            else if (!controllerNowConnected && _controllerWasConnected)
            {
                _fileLogger?.Info("Controller disconnected - ready for reconnection");
                _controllerWasConnected = false;
            }
        }

        private void DelayedTrigger(int delayMs, Action action)
        {
            DispatcherTimer timer = null;
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
            EventHandler handler = null;
            handler = (s, e) =>
            {
                timer.Stop();
                timer.Tick -= handler;
                action();
            };
            timer.Tick += handler;
            timer.Start();
        }

        private void TriggerFullscreenSwitch(FullscreenTriggerSource source, string controllerName = null)
        {
            _popupShowing = true;
            _fileLogger?.Info($"TriggerFullscreenSwitch called with source: {source}");

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

                    // Clear pressed buttons and set cooldown
                    _pressedButtons.Clear();
                    _dialogClosedTime = DateTime.Now;
                    _hotkeyTriggered = false;
                    _hotkeyPressStartTime = DateTime.MinValue;
                    _hotkeyLongPressTriggered = false;

                    _fileLogger?.Info($"Dialog closed with result: {result}, UserSelectedYes: {_activeDialog?.UserSelectedYes}");
                    _popupShowing = false;

                    if (result == true && _activeDialog.UserSelectedYes)
                    {
                        _fileLogger?.Info("User selected Yes - switching to fullscreen");
                        DelayedTrigger(50, () => SwitchToFullscreen());
                    }
                    else
                    {
                        _fileLogger?.Info("User selected Cancel or dialog timed out");
                    }

                    _activeDialog = null;
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"Error triggering fullscreen: {ex.Message}");
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
            }
            else
            {
                _fileLogger?.Error($"Fullscreen app not found: {fullscreenExe}");
            }
        }

        /// <summary>
        /// Called after settings are saved to restart monitoring if needed.
        /// </summary>
        public void OnSettingsChanged()
        {
            var currentMode = PlayniteApi.ApplicationInfo.Mode;
            if (currentMode == ApplicationMode.Fullscreen)
            {
                _fileLogger?.Info("Settings changed but in fullscreen mode, ignoring");
                return;
            }

            var newTriggerMode = Settings.Settings.FullscreenTriggerMode;
            var newEnableHotkey = Settings.Settings.EnableHotkey;

            bool modeChanged = newTriggerMode != _lastTriggerMode;
            bool hotkeyChanged = newEnableHotkey != _lastEnableHotkey;

            if (!modeChanged && !hotkeyChanged)
            {
                _fileLogger?.Info("Settings changed but trigger mode and hotkey unchanged");
                return;
            }

            _fileLogger?.Info($"Settings changed: TriggerMode {_lastTriggerMode} -> {newTriggerMode}, Hotkey {_lastEnableHotkey} -> {newEnableHotkey}");

            // Stop existing monitoring
            StopConnectionMonitoring();

            // Reset state
            _controllerWasConnected = XInputWrapper.IsControllerConnected();
            _pressedButtons.Clear();
            _hotkeyTriggered = false;

            // Update tracking
            _lastTriggerMode = newTriggerMode;
            _lastEnableHotkey = newEnableHotkey;

            // Restart connection monitoring if needed
            if (newTriggerMode != FullscreenTriggerMode.Disabled &&
                newTriggerMode != FullscreenTriggerMode.StartupOnly)
            {
                StartConnectionMonitoring();
            }
        }
    }
}
