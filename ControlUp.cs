using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using ControlUp.Common;
using ControlUp.Dialogs;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace ControlUp
{
    public class ControlUpPlugin : GenericPlugin
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        // Monitoring
        private CancellationTokenSource _hotkeyCts;
        private Task _hotkeyTask;
        private DispatcherTimer _connectionTimer;

        // Controller state tracking
        private bool _controllerWasConnected = false;
        private string _lastControllerName = null;

        // Popup state - controls SDL ownership
        // IMPORTANT: SDL is not thread-safe. Only one component should use SDL at a time.
        // When _popupShowing is true, the dialog owns SDL exclusively.
        private volatile bool _popupShowing = false;
        private DateTime _popupClosedTime = DateTime.MinValue;
        private const int SDL_COOLDOWN_MS = 500; // Wait after popup closes before using SDL

        // Hotkey tracking
        private volatile bool _hotkeyWasTriggered = false;
        private DateTime _hotkeyPressStartTime = DateTime.MinValue;
        private bool _hotkeyLongPressTriggered = false;

        // Components
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
                _fileLogger.Info($"=== ControlUp v{version} Starting ===");

                // Share logger with static classes for diagnostics
                ControllerDetector.Logger = _fileLogger;
                DirectInputWrapper.Logger = _fileLogger;
                SdlControllerWrapper.Logger = _fileLogger;
                ControlUp.Dialogs.ControllerDetectedDialog.Logger = _fileLogger;
                _fileLogger.Info("Loggers initialized for ControllerDetector, DirectInputWrapper, SDL, and Dialog");
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
            _fileLogger?.Info($"Settings: TriggerMode={triggerMode}, Hotkey={Settings.Settings.EnableHotkey}");

            if (currentMode == ApplicationMode.Fullscreen)
            {
                _fileLogger?.Info("Already in fullscreen mode, monitoring disabled");
                return;
            }

            // Check if this is a startup-only mode
            bool isStartupMode = triggerMode == FullscreenTriggerMode.StartupOnly;

            // Check initial controller state
            var controllerState = GetControllerStateForMode(triggerMode);
            _fileLogger?.Info($"Initial state: Connected={controllerState.IsConnected}, Name={controllerState.Name}");

            if (isStartupMode)
            {
                // Startup modes: only trigger at startup, then just monitor hotkeys
                _controllerWasConnected = controllerState.IsConnected;
                _lastControllerName = controllerState.Name;

                if (_controllerWasConnected)
                {
                    _fileLogger?.Info($"Startup mode: Controller detected, triggering fullscreen");
                    DelayedTrigger(500, () => TriggerFullscreenSwitch(FullscreenTriggerSource.Connection, _lastControllerName));
                }

                // Start hotkey monitoring if enabled (no connection monitoring for startup modes)
                if (Settings.Settings.EnableHotkey)
                    StartHotkeyMonitoring();

                // Store current settings for change detection
                _lastTriggerMode = triggerMode;
                _lastEnableHotkey = Settings.Settings.EnableHotkey;
                return;
            }

            // Runtime modes (NewConnectionOnly, AnyControllerAnytime):
            // For AnyControllerAnytime, trigger if controller already connected at startup
            // For NewConnectionOnly, only trigger on NEW connections (not already connected at startup)
            if (controllerState.IsConnected)
            {
                _controllerWasConnected = true;
                _lastControllerName = controllerState.Name;

                if (triggerMode == FullscreenTriggerMode.AnyControllerAnytime)
                {
                    _fileLogger?.Info($"AnyControllerAnytime: Controller already connected at startup, triggering fullscreen");
                    DelayedTrigger(500, () => TriggerFullscreenSwitch(FullscreenTriggerSource.Connection, controllerState.Name));
                }
                else
                {
                    // NewConnectionOnly: Don't trigger for already-connected controllers
                    _fileLogger?.Info($"NewConnectionOnly: Controller already connected at startup, waiting for reconnection");
                }
            }
            else
            {
                // No controller connected - start monitoring for connections
                _controllerWasConnected = false;
                _lastControllerName = null;
                _fileLogger?.Info($"Runtime mode: No controller at startup, will monitor for connections");
            }

            // Start monitoring (hotkeys + connection detection for runtime modes)
            StartMonitoring();

            // Store current settings for change detection
            _lastTriggerMode = triggerMode;
            _lastEnableHotkey = Settings.Settings.EnableHotkey;
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            _fileLogger?.Info("=== Application Stopped ===");
            StopMonitoring();
        }

        public override ISettings GetSettings(bool firstRunSettings) => Settings;

        public override System.Windows.Controls.UserControl GetSettingsView(bool firstRunView)
        {
            return new ControlUpSettingsView(Settings);
        }

        private ControllerDetector.ControllerState GetControllerStateForMode(FullscreenTriggerMode mode)
        {
            // All simplified modes detect any controller type (XInput + DirectInput/HID)
            return ControllerDetector.GetControllerState(xinputOnly: false);
        }

        private void StartMonitoring()
        {
            if (Settings.Settings.EnableHotkey)
                StartHotkeyMonitoring();

            var triggerMode = Settings.Settings.FullscreenTriggerMode;
            bool needsConnectionMonitoring = triggerMode == FullscreenTriggerMode.NewConnectionOnly ||
                                              triggerMode == FullscreenTriggerMode.AnyControllerAnytime;

            if (needsConnectionMonitoring)
            {
                // Connection monitoring runs at a slower interval than hotkey polling
                // to reduce resource usage - controller connections don't need sub-second detection
                const int CONNECTION_CHECK_INTERVAL_MS = 1000;
                _connectionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(CONNECTION_CHECK_INTERVAL_MS) };
                _connectionTimer.Tick += OnConnectionTimerTick;
                _connectionTimer.Start();
                _fileLogger?.Info($"Started connection monitoring ({CONNECTION_CHECK_INTERVAL_MS}ms interval)");
            }
        }

        private void StartHotkeyMonitoring()
        {
            // Initialize SDL for non-XInput controllers (8BitDo, PlayStation, etc.)
            // SDL will be used as fallback when no XInput controller is detected
            if (SdlControllerWrapper.Initialize())
            {
                _fileLogger?.Info("SDL initialized for non-XInput controller hotkey detection");
            }

            var interval = Settings.Settings.HotkeyPollingIntervalMs;
            _hotkeyCts = new CancellationTokenSource();
            _hotkeyTask = Task.Run(() => HotkeyPollingLoop(interval, _hotkeyCts.Token));
            _fileLogger?.Info($"Started hotkey monitoring ({interval}ms interval)");
        }

        private void StopMonitoring()
        {
            if (_hotkeyCts != null)
            {
                _hotkeyCts.Cancel();
                try { _hotkeyTask?.Wait(500); } catch { }
                _hotkeyCts.Dispose();
                _hotkeyCts = null;
                _hotkeyTask = null;
            }

            if (_connectionTimer != null)
            {
                _connectionTimer.Stop();
                _connectionTimer.Tick -= OnConnectionTimerTick;
                _connectionTimer = null;
            }

            // Release SDL resources
            try
            {
                SdlControllerWrapper.CloseController();
                SdlControllerWrapper.Shutdown();
            }
            catch { }

            // Release DirectInput/HID resources
            try
            {
                DirectInputWrapper.Cleanup();
            }
            catch { }

            _fileLogger?.Info("Stopped monitoring and released controller resources");
        }

        private void HotkeyPollingLoop(int intervalMs, CancellationToken token)
        {
            // Note: We primarily use XInput for hotkey detection since it's thread-safe.
            // SDL is used as fallback for non-XInput controllers (like DualSense via Bluetooth).
            // SDL calls are protected by locks but we avoid calling it when dialog is open
            // since the dialog also uses SDL for navigation.

            int consecutiveErrors = 0;
            const int MAX_CONSECUTIVE_ERRORS = 10;
            int loopIterations = 0;
            DateTime lastLoopLog = DateTime.Now;

            _fileLogger?.Info($"[HotkeyLoop] Started with {intervalMs}ms interval");

            while (!token.IsCancellationRequested)
            {
                loopIterations++;

                // Log every 30 seconds
                if ((DateTime.Now - lastLoopLog).TotalSeconds >= 30)
                {
                    lastLoopLog = DateTime.Now;
                    _fileLogger?.Debug($"[HotkeyLoop] {loopIterations} iterations completed");
                }
                try
                {
                    if (PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen || _popupShowing)
                    {
                        Thread.Sleep(intervalMs);
                        continue;
                    }

                    bool hotkeyPressed = false;
                    string controllerName = null;

                    // Check XInput controllers (use GetStateEx for Guide button support)
                    // XInput is thread-safe and works for Xbox controllers + many third-party controllers
                    ushort xinputButtons = 0;
                    bool hasXInputController = false;

                    for (uint slot = 0; slot < 4; slot++)
                    {
                        try
                        {
                            XInputWrapper.XINPUT_STATE state = new XInputWrapper.XINPUT_STATE();
                            if (XInputWrapper.GetStateEx(slot, ref state) == XInputWrapper.ERROR_SUCCESS)
                            {
                                hasXInputController = true;
                                xinputButtons |= state.Gamepad.wButtons;
                            }
                        }
                        catch
                        {
                            // Individual slot read failed, continue to next
                        }
                    }

                    if (hasXInputController)
                    {
                        hotkeyPressed = IsHotkeyPressed(xinputButtons, Settings.Settings.HotkeyCombo);
                        if (hotkeyPressed)
                        {
                            try
                            {
                                controllerName = XInputWrapper.GetControllerName();
                            }
                            catch
                            {
                                controllerName = "Xbox Controller";
                            }
                        }
                    }

                    // Use SDL as fallback for any non-XInput controller (8BitDo, PlayStation, etc.)
                    // SDL has mappings for thousands of controllers via gamecontrollerdb.txt
                    if (!hotkeyPressed && !hasXInputController && !_popupShowing)
                    {
                        try
                        {
                            var sdlReading = SdlControllerWrapper.GetCurrentReading();
                            if (sdlReading.IsValid)
                            {
                                hotkeyPressed = IsSdlHotkeyPressed(sdlReading.Buttons, Settings.Settings.HotkeyCombo);
                                if (hotkeyPressed)
                                {
                                    controllerName = SdlControllerWrapper.GetControllerName() ?? "Controller";
                                }
                            }
                        }
                        catch (Exception sdlEx)
                        {
                            _fileLogger?.Error($"SDL error in hotkey loop: {sdlEx.Message}");
                        }
                    }

                    if (!hotkeyPressed)
                    {
                        // Hotkey released - reset tracking
                        _hotkeyWasTriggered = false;
                        _hotkeyPressStartTime = DateTime.MinValue;
                        _hotkeyLongPressTriggered = false;
                    }
                    else
                    {
                        // Hotkey is currently pressed
                        bool requireLongPress = Settings.Settings.RequireLongPress;
                        int longPressDelayMs = Settings.Settings.LongPressDelayMs;

                        if (requireLongPress)
                        {
                            // Long press mode
                            if (_hotkeyPressStartTime == DateTime.MinValue)
                            {
                                // Just started pressing
                                _hotkeyPressStartTime = DateTime.Now;
                            }
                            else if (!_hotkeyLongPressTriggered)
                            {
                                // Check if held long enough
                                double heldMs = (DateTime.Now - _hotkeyPressStartTime).TotalMilliseconds;
                                if (heldMs >= longPressDelayMs)
                                {
                                    _hotkeyLongPressTriggered = true;
                                    _fileLogger?.Info($"Hotkey {Settings.Settings.HotkeyCombo} long-pressed for {heldMs:F0}ms (Controller: {controllerName})");

                                    string finalName = controllerName;
                                    var dispatcher = Application.Current?.Dispatcher;
                                    if (dispatcher != null && !dispatcher.HasShutdownStarted)
                                    {
                                        dispatcher.BeginInvoke(new Action(() =>
                                        {
                                            TriggerFullscreenSwitch(FullscreenTriggerSource.Hotkey, finalName);
                                        }));
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Instant tap mode (original behavior)
                            if (!_hotkeyWasTriggered)
                            {
                                _hotkeyWasTriggered = true;
                                _fileLogger?.Info($"Hotkey {Settings.Settings.HotkeyCombo} pressed (Controller: {controllerName})");

                                string finalName = controllerName;
                                var dispatcher = Application.Current?.Dispatcher;
                                if (dispatcher != null && !dispatcher.HasShutdownStarted)
                                {
                                    dispatcher.BeginInvoke(new Action(() =>
                                    {
                                        TriggerFullscreenSwitch(FullscreenTriggerSource.Hotkey, finalName);
                                    }));
                                }
                            }
                        }
                    }

                    // Reset error counter on successful iteration
                    consecutiveErrors = 0;
                }
                catch (Exception ex)
                {
                    consecutiveErrors++;
                    _fileLogger?.Error($"Hotkey polling error ({consecutiveErrors}): {ex.Message}");

                    // If we hit too many consecutive errors, slow down to prevent CPU spin
                    if (consecutiveErrors >= MAX_CONSECUTIVE_ERRORS)
                    {
                        _fileLogger?.Error("Too many consecutive errors in hotkey loop, backing off...");
                        Thread.Sleep(1000); // Back off for 1 second
                        consecutiveErrors = 0;
                    }
                }

                Thread.Sleep(intervalMs);
            }
        }

        // Track connection timer ticks for diagnostics
        private int _connectionTimerTickCount = 0;
        private DateTime _pluginStartTime = DateTime.Now;

        private void OnConnectionTimerTick(object sender, EventArgs e)
        {
            _connectionTimerTickCount++;

            try
            {
                if (PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen)
                    return;

                if (_popupShowing)
                {
                    // Don't log every tick, this would spam
                    return;
                }

                CheckControllerState();
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"Connection timer error: {ex.Message}");
            }
        }

        // For verbose debugging - track last logged state to avoid spam
        private static DateTime _lastVerboseLog = DateTime.MinValue;

        private void CheckControllerState()
        {
            var triggerMode = Settings.Settings.FullscreenTriggerMode;

            // Skip disabled and startup modes - they only check at startup
            if (triggerMode == FullscreenTriggerMode.Disabled ||
                triggerMode == FullscreenTriggerMode.StartupOnly)
            {
                return;
            }

            var state = GetControllerStateForMode(triggerMode);

            // Verbose logging every 10 seconds for debugging
            if (Settings.Settings.EnableLogging && (DateTime.Now - _lastVerboseLog).TotalSeconds >= 10)
            {
                _lastVerboseLog = DateTime.Now;
                var uptime = (DateTime.Now - _pluginStartTime).TotalSeconds;
                _fileLogger?.Info($"[Poll] tick#{_connectionTimerTickCount}, uptime={uptime:F0}s, Connected={state.IsConnected}, Name={state.Name}, Source={state.Source}, WasConnected={_controllerWasConnected}");
            }

            // Detect NEW connection
            if (state.IsConnected && !_controllerWasConnected)
            {
                _fileLogger?.Info($"New controller detected: {state.Name} (Source: {state.Source})");
                _controllerWasConnected = true;
                _lastControllerName = state.Name;
                TriggerFullscreenSwitch(FullscreenTriggerSource.Connection, state.Name);
            }
            else if (!state.IsConnected && _controllerWasConnected)
            {
                _fileLogger?.Info("Controller disconnected - ready for reconnection");
                _controllerWasConnected = false;
                _lastControllerName = null;
            }
        }

        private bool IsHotkeyPressed(ushort buttons, ControllerHotkey hotkey)
        {
            switch (hotkey)
            {
                // Combo hotkeys - Start combos
                case ControllerHotkey.StartPlusRB:
                    return (buttons & XInputWrapper.XINPUT_GAMEPAD_START) != 0 &&
                           (buttons & XInputWrapper.XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0;
                case ControllerHotkey.StartPlusLB:
                    return (buttons & XInputWrapper.XINPUT_GAMEPAD_START) != 0 &&
                           (buttons & XInputWrapper.XINPUT_GAMEPAD_LEFT_SHOULDER) != 0;
                case ControllerHotkey.StartPlusBack:
                    return (buttons & XInputWrapper.XINPUT_GAMEPAD_START) != 0 &&
                           (buttons & XInputWrapper.XINPUT_GAMEPAD_BACK) != 0;
                // Combo hotkeys - Back combos
                case ControllerHotkey.BackPlusStart:
                    return (buttons & XInputWrapper.XINPUT_GAMEPAD_BACK) != 0 &&
                           (buttons & XInputWrapper.XINPUT_GAMEPAD_START) != 0;
                case ControllerHotkey.BackPlusRB:
                    return (buttons & XInputWrapper.XINPUT_GAMEPAD_BACK) != 0 &&
                           (buttons & XInputWrapper.XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0;
                case ControllerHotkey.BackPlusLB:
                    return (buttons & XInputWrapper.XINPUT_GAMEPAD_BACK) != 0 &&
                           (buttons & XInputWrapper.XINPUT_GAMEPAD_LEFT_SHOULDER) != 0;
                // Guide button combo hotkeys - available via XInputGetStateEx
                case ControllerHotkey.GuidePlusStart:
                    return (buttons & XInputWrapper.XINPUT_GAMEPAD_GUIDE) != 0 &&
                           (buttons & XInputWrapper.XINPUT_GAMEPAD_START) != 0;
                case ControllerHotkey.GuidePlusBack:
                    return (buttons & XInputWrapper.XINPUT_GAMEPAD_GUIDE) != 0 &&
                           (buttons & XInputWrapper.XINPUT_GAMEPAD_BACK) != 0;
                case ControllerHotkey.GuidePlusRB:
                    return (buttons & XInputWrapper.XINPUT_GAMEPAD_GUIDE) != 0 &&
                           (buttons & XInputWrapper.XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0;
                case ControllerHotkey.GuidePlusLB:
                    return (buttons & XInputWrapper.XINPUT_GAMEPAD_GUIDE) != 0 &&
                           (buttons & XInputWrapper.XINPUT_GAMEPAD_LEFT_SHOULDER) != 0;
                // Shoulder button combos
                case ControllerHotkey.LBPlusRB:
                    return (buttons & XInputWrapper.XINPUT_GAMEPAD_LEFT_SHOULDER) != 0 &&
                           (buttons & XInputWrapper.XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0;
                case ControllerHotkey.LBPlusRBPlusStart:
                    return (buttons & XInputWrapper.XINPUT_GAMEPAD_LEFT_SHOULDER) != 0 &&
                           (buttons & XInputWrapper.XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0 &&
                           (buttons & XInputWrapper.XINPUT_GAMEPAD_START) != 0;
                case ControllerHotkey.LBPlusRBPlusBack:
                    return (buttons & XInputWrapper.XINPUT_GAMEPAD_LEFT_SHOULDER) != 0 &&
                           (buttons & XInputWrapper.XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0 &&
                           (buttons & XInputWrapper.XINPUT_GAMEPAD_BACK) != 0;
                // Single button hotkeys
                case ControllerHotkey.Guide:
                    return (buttons & XInputWrapper.XINPUT_GAMEPAD_GUIDE) != 0;
                case ControllerHotkey.Back:
                    return (buttons & XInputWrapper.XINPUT_GAMEPAD_BACK) != 0;
                case ControllerHotkey.Start:
                    return (buttons & XInputWrapper.XINPUT_GAMEPAD_START) != 0;
                default:
                    return false;
            }
        }

        private bool IsSdlHotkeyPressed(SdlControllerWrapper.SdlButtons buttons, ControllerHotkey hotkey)
        {
            switch (hotkey)
            {
                // Combo hotkeys - Start combos
                case ControllerHotkey.StartPlusRB:
                    return (buttons & SdlControllerWrapper.SdlButtons.Start) != 0 &&
                           (buttons & SdlControllerWrapper.SdlButtons.RightShoulder) != 0;
                case ControllerHotkey.StartPlusLB:
                    return (buttons & SdlControllerWrapper.SdlButtons.Start) != 0 &&
                           (buttons & SdlControllerWrapper.SdlButtons.LeftShoulder) != 0;
                case ControllerHotkey.StartPlusBack:
                    return (buttons & SdlControllerWrapper.SdlButtons.Start) != 0 &&
                           (buttons & SdlControllerWrapper.SdlButtons.Back) != 0;
                // Combo hotkeys - Back combos
                case ControllerHotkey.BackPlusStart:
                    return (buttons & SdlControllerWrapper.SdlButtons.Back) != 0 &&
                           (buttons & SdlControllerWrapper.SdlButtons.Start) != 0;
                case ControllerHotkey.BackPlusRB:
                    return (buttons & SdlControllerWrapper.SdlButtons.Back) != 0 &&
                           (buttons & SdlControllerWrapper.SdlButtons.RightShoulder) != 0;
                case ControllerHotkey.BackPlusLB:
                    return (buttons & SdlControllerWrapper.SdlButtons.Back) != 0 &&
                           (buttons & SdlControllerWrapper.SdlButtons.LeftShoulder) != 0;
                // Guide button combo hotkeys
                case ControllerHotkey.GuidePlusStart:
                    return (buttons & SdlControllerWrapper.SdlButtons.Guide) != 0 &&
                           (buttons & SdlControllerWrapper.SdlButtons.Start) != 0;
                case ControllerHotkey.GuidePlusBack:
                    return (buttons & SdlControllerWrapper.SdlButtons.Guide) != 0 &&
                           (buttons & SdlControllerWrapper.SdlButtons.Back) != 0;
                case ControllerHotkey.GuidePlusRB:
                    return (buttons & SdlControllerWrapper.SdlButtons.Guide) != 0 &&
                           (buttons & SdlControllerWrapper.SdlButtons.RightShoulder) != 0;
                case ControllerHotkey.GuidePlusLB:
                    return (buttons & SdlControllerWrapper.SdlButtons.Guide) != 0 &&
                           (buttons & SdlControllerWrapper.SdlButtons.LeftShoulder) != 0;
                // Shoulder button combos
                case ControllerHotkey.LBPlusRB:
                    return (buttons & SdlControllerWrapper.SdlButtons.LeftShoulder) != 0 &&
                           (buttons & SdlControllerWrapper.SdlButtons.RightShoulder) != 0;
                case ControllerHotkey.LBPlusRBPlusStart:
                    return (buttons & SdlControllerWrapper.SdlButtons.LeftShoulder) != 0 &&
                           (buttons & SdlControllerWrapper.SdlButtons.RightShoulder) != 0 &&
                           (buttons & SdlControllerWrapper.SdlButtons.Start) != 0;
                case ControllerHotkey.LBPlusRBPlusBack:
                    return (buttons & SdlControllerWrapper.SdlButtons.LeftShoulder) != 0 &&
                           (buttons & SdlControllerWrapper.SdlButtons.RightShoulder) != 0 &&
                           (buttons & SdlControllerWrapper.SdlButtons.Back) != 0;
                // Single button hotkeys
                case ControllerHotkey.Guide:
                    return (buttons & SdlControllerWrapper.SdlButtons.Guide) != 0;
                case ControllerHotkey.Back:
                    return (buttons & SdlControllerWrapper.SdlButtons.Back) != 0;
                case ControllerHotkey.Start:
                    return (buttons & SdlControllerWrapper.SdlButtons.Start) != 0;
                default:
                    return false;
            }
        }

        private bool IsHidHotkeyPressed(DirectInputWrapper.HidControllerReading reading, ControllerHotkey hotkey)
        {
            // PlayStation button mapping:
            // Options = Start/Menu, Share = Back/View, PS = Guide
            // R1 = RB, L1 = LB
            switch (hotkey)
            {
                // Combo hotkeys
                case ControllerHotkey.StartPlusRB:
                    return reading.Options && reading.R1;
                case ControllerHotkey.StartPlusLB:
                    return reading.Options && reading.L1;
                case ControllerHotkey.BackPlusStart:
                    return reading.Share && reading.Options;
                case ControllerHotkey.BackPlusRB:
                    return reading.Share && reading.R1;
                case ControllerHotkey.BackPlusLB:
                    return reading.Share && reading.L1;
                // Guide button combo hotkeys
                case ControllerHotkey.GuidePlusStart:
                    return reading.PS && reading.Options;
                case ControllerHotkey.GuidePlusBack:
                    return reading.PS && reading.Share;
                case ControllerHotkey.GuidePlusRB:
                    return reading.PS && reading.R1;
                case ControllerHotkey.GuidePlusLB:
                    return reading.PS && reading.L1;
                // Shoulder button combos
                case ControllerHotkey.StartPlusBack:
                    return reading.Options && reading.Share;
                case ControllerHotkey.LBPlusRB:
                    return reading.L1 && reading.R1;
                case ControllerHotkey.LBPlusRBPlusStart:
                    return reading.L1 && reading.R1 && reading.Options;
                case ControllerHotkey.LBPlusRBPlusBack:
                    return reading.L1 && reading.R1 && reading.Share;
                // Single button hotkeys
                case ControllerHotkey.Guide:
                    return reading.PS;
                case ControllerHotkey.Back:
                    return reading.Share;
                case ControllerHotkey.Start:
                    return reading.Options;
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
                timer.Tick -= handler; // Unhook to allow GC
                action();
            };
            timer.Tick += handler;
            timer.Start();
        }

        private void TriggerFullscreenSwitch(FullscreenTriggerSource source, string controllerName = null)
        {
            _popupShowing = true;
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
                    var dialog = new ControllerDetectedDialog(Settings.Settings, source, controllerName);
                    var result = dialog.ShowDialog();

                    _fileLogger?.Info($"Dialog closed with result: {result}, UserSelectedYes: {dialog?.UserSelectedYes}");
                    _popupShowing = false;

                    if (result == true && dialog.UserSelectedYes)
                    {
                        _fileLogger?.Info("User selected Yes - waiting 50ms before switching to fullscreen");
                        // Wait for dialog to fully close before launching fullscreen
                        DelayedTrigger(50, () =>
                        {
                            _fileLogger?.Info("Delay complete - switching to fullscreen now");
                            SwitchToFullscreen();
                        });
                    }
                    else
                    {
                        _fileLogger?.Info("User selected Cancel or dialog timed out");
                    }
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"Error triggering fullscreen: {ex.Message}");
                _fileLogger?.Error($"Stack trace: {ex.StackTrace}");
                Logger.Error(ex, "ControlUp: Error triggering fullscreen switch");
                _popupShowing = false;
            }
        }

        private void SwitchToFullscreen()
        {
            string fullscreenExe = Path.Combine(PlayniteApi.Paths.ApplicationPath, "Playnite.FullscreenApp.exe");
            _fileLogger?.Info($"Launching: {fullscreenExe}");

            if (File.Exists(fullscreenExe))
            {
                // Just launch - Playnite's internal pipe system handles the mode switch
                // Don't call Shutdown() - let Playnite coordinate the transition
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

            // Stop all existing monitoring
            StopMonitoring();

            // Reset state
            _controllerWasConnected = false;
            _lastControllerName = null;

            // Update tracking
            _lastTriggerMode = newTriggerMode;
            _lastEnableHotkey = newEnableHotkey;

            // Restart monitoring based on new settings
            if (newTriggerMode == FullscreenTriggerMode.Disabled)
            {
                // Only start hotkey monitoring if enabled
                if (newEnableHotkey)
                    StartHotkeyMonitoring();
                return;
            }

            bool isStartupMode = newTriggerMode == FullscreenTriggerMode.StartupOnly;

            if (isStartupMode)
            {
                // Startup modes: only hotkey monitoring (no connection monitoring mid-session)
                if (newEnableHotkey)
                    StartHotkeyMonitoring();
                _fileLogger?.Info("Startup mode selected - connection monitoring disabled until next app restart");
            }
            else
            {
                // Runtime modes: full monitoring
                var controllerState = GetControllerStateForMode(newTriggerMode);
                _controllerWasConnected = controllerState.IsConnected;
                _lastControllerName = controllerState.Name;
                _fileLogger?.Info($"Runtime mode: Current state - Connected={_controllerWasConnected}, Name={_lastControllerName}");
                StartMonitoring();
            }
        }
    }
}
