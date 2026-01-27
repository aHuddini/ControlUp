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

        // Connection state tracking (for NewConnectionOnly mode)
        private bool _controllerWasConnected = false;

        // Popup state
        private volatile bool _popupShowing = false;
        private DateTime _dialogClosedTime = DateTime.MinValue;

        // Hotkey tracking via SDK events
        private HashSet<ControllerInput> _pressedButtons = new HashSet<ControllerInput>();
        private volatile bool _hotkeyTriggered = false;
        private DateTime _hotkeyPressStartTime = DateTime.MinValue;
        private bool _hotkeyLongPressTriggered = false;
        private string _currentHotkeyControllerName = null;  // Name of controller currently pressing hotkey combo
        private DispatcherTimer _longPressTimer = null;

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
                _fileLogger.Info($"=== ControlUp v{version} Starting (SDK 6.15 event-based) ===");

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

            // Check initial controller state using SDK API + HID fallback
            var connectedControllers = PlayniteApi.GetConnectedControllers();
            bool sdkConnected = connectedControllers != null && connectedControllers.Count > 0;
            bool hidConnected = HidControllerDetector.IsAnyControllerConnected();
            _controllerWasConnected = sdkConnected || hidConnected;

            // Get controller name from SDK or HID (HID includes connection type)
            string controllerName = null;
            if (sdkConnected && connectedControllers.Count > 0)
            {
                controllerName = connectedControllers[0].Name;
            }
            else if (hidConnected)
            {
                var hidControllers = HidControllerDetector.GetConnectedControllers();
                if (hidControllers.Count > 0)
                    controllerName = hidControllers[0].DisplayName;  // Includes "(USB)" or "(Bluetooth)"
            }

            _fileLogger?.Info($"Initial controller state: SDK={sdkConnected}, HID={hidConnected}, Name={controllerName}");

            // Store current settings for change detection
            _lastTriggerMode = triggerMode;
            _lastEnableHotkey = Settings.Settings.EnableHotkey;

            // Handle startup trigger modes
            if (triggerMode == FullscreenTriggerMode.StartupOnly)
            {
                if (_controllerWasConnected)
                {
                    _fileLogger?.Info("StartupOnly mode: Controller detected, triggering fullscreen");
                    DelayedTrigger(500, () => TriggerFullscreenSwitch(FullscreenTriggerSource.Connection, controllerName));
                }
                return;
            }

            if (_controllerWasConnected && triggerMode == FullscreenTriggerMode.AnyControllerAnytime)
            {
                _fileLogger?.Info("AnyControllerAnytime: Controller already connected at startup, triggering fullscreen");
                DelayedTrigger(500, () => TriggerFullscreenSwitch(FullscreenTriggerSource.Connection, controllerName));
            }

            // SDK events handle connection detection - no polling needed
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            _fileLogger?.Info("=== Application Stopped ===");
        }

        public override ISettings GetSettings(bool firstRunSettings) => Settings;

        public override System.Windows.Controls.UserControl GetSettingsView(bool firstRunView)
        {
            return new ControlUpSettingsView(Settings);
        }

        /// <summary>
        /// SDK callback - fired when a controller is connected.
        /// This is the primary mechanism for connection detection.
        /// </summary>
        public override void OnControllerConnected(OnControllerConnectedArgs args)
        {
            var controllerName = args.Controller?.Name;
            var controllerId = args.Controller?.InstanceId;
            _fileLogger?.Info($"SDK OnControllerConnected: '{controllerName}' (ID: {controllerId})");

            // Don't trigger if in fullscreen, popup showing, or disabled
            if (PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen || _popupShowing)
            {
                _fileLogger?.Info("Ignoring connection - fullscreen or popup showing");
                return;
            }

            var triggerMode = Settings.Settings.FullscreenTriggerMode;
            if (triggerMode == FullscreenTriggerMode.Disabled ||
                triggerMode == FullscreenTriggerMode.StartupOnly)
            {
                _fileLogger?.Info($"Ignoring connection - trigger mode is {triggerMode}");
                _controllerWasConnected = true;
                return;
            }

            // NewConnectionOnly: only trigger if wasn't connected before
            // AnyControllerAnytime: always trigger
            bool shouldTrigger = triggerMode == FullscreenTriggerMode.AnyControllerAnytime ||
                                 (triggerMode == FullscreenTriggerMode.NewConnectionOnly && !_controllerWasConnected);

            _controllerWasConnected = true;

            if (shouldTrigger)
            {
                // Delay popup slightly to allow Playnite's SDL input to fully initialize the controller
                // This ensures button events will work in the dialog
                var name = controllerName;
                _fileLogger?.Info($"Triggering fullscreen switch for controller: {name} (with 300ms delay for SDL init)");
                DelayedTrigger(300, () => TriggerFullscreenSwitch(FullscreenTriggerSource.Connection, name));
            }
        }

        /// <summary>
        /// SDK callback - fired when a controller is disconnected.
        /// </summary>
        public override void OnControllerDisconnected(OnControllerDisconnectedArgs args)
        {
            var controllerName = args.Controller?.Name;
            _fileLogger?.Info($"SDK OnControllerDisconnected: {controllerName}");

            // Check if any controllers are still connected
            var connectedControllers = PlayniteApi.GetConnectedControllers();
            bool anyConnected = (connectedControllers != null && connectedControllers.Count > 0) ||
                               HidControllerDetector.IsAnyControllerConnected();

            if (!anyConnected)
            {
                _fileLogger?.Info("All controllers disconnected - ready for reconnection");
                _controllerWasConnected = false;
            }
        }

        /// <summary>
        /// SDK callback - receives all controller button events from Playnite's SDL input system.
        /// </summary>
        public override void OnDesktopControllerButtonStateChanged(OnControllerButtonStateChangedArgs args)
        {
            // Log controller info for diagnostics
            var ctrlName = args.Controller?.Name ?? "null";
            var ctrlId = args.Controller?.InstanceId;
            _fileLogger?.Debug($"Button event: {args.Button} {args.State} from '{ctrlName}' (ID:{ctrlId})");

            // Forward to active dialog if showing
            // Use BeginInvoke (async) to avoid deadlock with ShowDialog blocking the UI thread
            if (_activeDialog != null && _popupShowing)
            {
                try
                {
                    var button = args.Button;
                    var state = args.State;
                    var dialog = _activeDialog;
                    _fileLogger?.Debug($"Forwarding {button} {state} to dialog");
                    Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        _fileLogger?.Debug($"BeginInvoke executing for {button} {state}");
                        dialog?.HandleControllerInput(button, state);
                    }));
                }
                catch (Exception ex)
                {
                    _fileLogger?.Error($"Error forwarding to dialog: {ex.Message}");
                }
                return;
            }

            // Track button states for hotkey combo detection
            // Get controller name - enrich generic "XInput Controller #X" names with better info from SDK
            string controllerName = GetEnrichedControllerName(args.Controller?.Name, args.Controller?.InstanceId);

            if (args.State == ControllerInputState.Pressed)
            {
                _pressedButtons.Add(args.Button);
            }
            else
            {
                _pressedButtons.Remove(args.Button);
                _hotkeyTriggered = false;
                _hotkeyPressStartTime = DateTime.MinValue;
                _hotkeyLongPressTriggered = false;
                _currentHotkeyControllerName = null;
                StopLongPressTimer();
            }

            // Don't check hotkeys if disabled or already in fullscreen
            if (!Settings.Settings.EnableHotkey ||
                PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen ||
                _popupShowing)
            {
                return;
            }

            // Prevent immediate re-trigger after dialog closes (0 = no cooldown)
            int cooldownMs = Settings.Settings.HotkeyCooldownMs;
            if (cooldownMs > 0 && (DateTime.Now - _dialogClosedTime).TotalMilliseconds < cooldownMs)
            {
                return;
            }

            // Check for hotkey combo
            if (IsHotkeyComboPressed())
            {
                bool requireLongPress = Settings.Settings.RequireLongPress;

                // Store controller name when combo is first detected (use for long press)
                if (_currentHotkeyControllerName == null && !string.IsNullOrEmpty(controllerName))
                {
                    _currentHotkeyControllerName = controllerName;
                }

                if (requireLongPress)
                {
                    // Start long press timer if not already running
                    if (_longPressTimer == null && !_hotkeyLongPressTriggered)
                    {
                        int longPressDelayMs = Settings.Settings.LongPressDelayMs;
                        _hotkeyPressStartTime = DateTime.Now;
                        _fileLogger?.Debug($"Hotkey combo held - starting long press timer ({longPressDelayMs}ms required), controller: {_currentHotkeyControllerName}");

                        // Create timer to check long press completion
                        _longPressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                        _longPressTimer.Tick += OnLongPressTimerTick;
                        _longPressTimer.Start();
                    }
                }
                else
                {
                    // Instant tap mode - use name from current event
                    if (!_hotkeyTriggered)
                    {
                        _hotkeyTriggered = true;
                        var nameToUse = !string.IsNullOrEmpty(controllerName) ? controllerName : _currentHotkeyControllerName;
                        _fileLogger?.Info($"Hotkey {Settings.Settings.HotkeyCombo} detected via SDK (controller: {nameToUse})");
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            TriggerFullscreenSwitch(FullscreenTriggerSource.Hotkey, nameToUse);
                        }));
                    }
                }
            }
            else
            {
                // Combo no longer pressed - stop long press timer and clear controller name
                _currentHotkeyControllerName = null;
                StopLongPressTimer();
            }
        }

        private void OnLongPressTimerTick(object sender, EventArgs e)
        {
            // Check if combo is still pressed
            if (!IsHotkeyComboPressed())
            {
                _fileLogger?.Debug("Long press cancelled - combo released");
                _currentHotkeyControllerName = null;
                StopLongPressTimer();
                return;
            }

            // Check if long press duration reached
            int longPressDelayMs = Settings.Settings.LongPressDelayMs;
            double heldMs = (DateTime.Now - _hotkeyPressStartTime).TotalMilliseconds;

            if (heldMs >= longPressDelayMs && !_hotkeyLongPressTriggered)
            {
                _hotkeyLongPressTriggered = true;
                var controllerName = _currentHotkeyControllerName;
                _fileLogger?.Info($"Hotkey {Settings.Settings.HotkeyCombo} long-pressed for {heldMs:F0}ms (controller: {controllerName})");
                StopLongPressTimer();
                TriggerFullscreenSwitch(FullscreenTriggerSource.Hotkey, controllerName);
            }
        }

        private void StopLongPressTimer()
        {
            if (_longPressTimer != null)
            {
                _longPressTimer.Stop();
                _longPressTimer.Tick -= OnLongPressTimerTick;
                _longPressTimer = null;
            }
        }

        /// <summary>
        /// Gets a better controller name when the SDK returns generic "XInput Controller #X".
        /// Falls back to SDK's GetConnectedControllers() which has proper names from SDL database.
        /// </summary>
        private string GetEnrichedControllerName(string eventName, int? instanceId)
        {
            // If we got a good name from the event, use it
            if (!string.IsNullOrEmpty(eventName) && !eventName.StartsWith("XInput Controller"))
            {
                return eventName;
            }

            // Try to get a better name from GetConnectedControllers()
            try
            {
                var controllers = PlayniteApi.GetConnectedControllers();
                if (controllers != null && controllers.Count > 0)
                {
                    // If we have an instance ID, try to match it
                    if (instanceId.HasValue)
                    {
                        var match = controllers.FirstOrDefault(c => c.InstanceId == instanceId.Value);
                        if (match != null && !string.IsNullOrEmpty(match.Name) && !match.Name.StartsWith("XInput Controller"))
                        {
                            _fileLogger?.Debug($"Enriched controller name: '{eventName}' -> '{match.Name}' (matched by ID {instanceId})");
                            return match.Name;
                        }
                    }

                    // Otherwise, find the first controller with a non-generic name
                    var betterName = controllers.FirstOrDefault(c => !string.IsNullOrEmpty(c.Name) && !c.Name.StartsWith("XInput Controller"));
                    if (betterName != null)
                    {
                        _fileLogger?.Debug($"Enriched controller name: '{eventName}' -> '{betterName.Name}' (first non-XInput)");
                        return betterName.Name;
                    }
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Debug($"Error enriching controller name: {ex.Message}");
            }

            // Fall back to the original name
            return eventName;
        }

        private bool IsHotkeyComboPressed()
        {
            switch (Settings.Settings.HotkeyCombo)
            {
                case ControllerHotkey.StartPlusRB:
                    return _pressedButtons.Contains(ControllerInput.Start) &&
                           _pressedButtons.Contains(ControllerInput.RightShoulder);
                case ControllerHotkey.StartPlusLB:
                    return _pressedButtons.Contains(ControllerInput.Start) &&
                           _pressedButtons.Contains(ControllerInput.LeftShoulder);
                case ControllerHotkey.StartPlusBack:
                    return _pressedButtons.Contains(ControllerInput.Start) &&
                           _pressedButtons.Contains(ControllerInput.Back);
                case ControllerHotkey.BackPlusStart:
                    return _pressedButtons.Contains(ControllerInput.Back) &&
                           _pressedButtons.Contains(ControllerInput.Start);
                case ControllerHotkey.BackPlusRB:
                    return _pressedButtons.Contains(ControllerInput.Back) &&
                           _pressedButtons.Contains(ControllerInput.RightShoulder);
                case ControllerHotkey.BackPlusLB:
                    return _pressedButtons.Contains(ControllerInput.Back) &&
                           _pressedButtons.Contains(ControllerInput.LeftShoulder);
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
            _fileLogger?.Info($"TriggerFullscreenSwitch: source={source}, controller={controllerName}");

            try
            {
                bool skipPopup = source == FullscreenTriggerSource.Connection
                    ? Settings.Settings.SkipPopupOnConnection
                    : Settings.Settings.SkipPopupOnHotkey;

                if (skipPopup)
                {
                    _fileLogger?.Info($"Skipping popup, switching directly to fullscreen");
                    SwitchToFullscreen();
                    _popupShowing = false;
                }
                else
                {
                    _activeDialog = new ControllerDetectedDialog(Settings.Settings, source, controllerName);
                    var result = _activeDialog.ShowDialog();

                    _pressedButtons.Clear();
                    _dialogClosedTime = DateTime.Now;
                    _hotkeyTriggered = false;
                    _hotkeyPressStartTime = DateTime.MinValue;
                    _hotkeyLongPressTriggered = false;

                    _fileLogger?.Info($"Dialog closed: result={result}, UserSelectedYes={_activeDialog?.UserSelectedYes}");
                    _popupShowing = false;

                    if (result == true && _activeDialog.UserSelectedYes)
                    {
                        _fileLogger?.Info("User selected Yes - switching to fullscreen");
                        DelayedTrigger(50, () => SwitchToFullscreen());
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

            // Reset state using SDK API
            var connectedControllers = PlayniteApi.GetConnectedControllers();
            _controllerWasConnected = (connectedControllers != null && connectedControllers.Count > 0) ||
                                      HidControllerDetector.IsAnyControllerConnected();
            _pressedButtons.Clear();
            _hotkeyTriggered = false;

            _lastTriggerMode = newTriggerMode;
            _lastEnableHotkey = newEnableHotkey;
        }
    }
}
