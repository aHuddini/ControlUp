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

        // Popup state
        private bool _popupShowing = false;

        // Hotkey tracking
        private volatile bool _hotkeyWasTriggered = false;

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
            bool isStartupMode = triggerMode == FullscreenTriggerMode.XInputControllerOnStartup ||
                                  triggerMode == FullscreenTriggerMode.AnyControllerOnStartup;

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

            // Runtime modes (XInputController, AnyController):
            // If controller already connected at startup, trigger immediately
            if (controllerState.IsConnected)
            {
                _fileLogger?.Info($"Runtime mode: Controller already connected at startup, triggering fullscreen");
                _controllerWasConnected = true;
                _lastControllerName = controllerState.Name;
                DelayedTrigger(500, () => TriggerFullscreenSwitch(FullscreenTriggerSource.Connection, controllerState.Name));
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
            bool xinputOnly = mode == FullscreenTriggerMode.XInputController ||
                              mode == FullscreenTriggerMode.XInputControllerOnStartup;

            return ControllerDetector.GetControllerState(xinputOnly);
        }

        private void StartMonitoring()
        {
            if (Settings.Settings.EnableHotkey)
                StartHotkeyMonitoring();

            var triggerMode = Settings.Settings.FullscreenTriggerMode;
            bool needsConnectionMonitoring = triggerMode == FullscreenTriggerMode.XInputController ||
                                              triggerMode == FullscreenTriggerMode.AnyController;

            if (needsConnectionMonitoring)
            {
                var interval = Settings.Settings.HotkeyPollingIntervalMs;
                _connectionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(interval) };
                _connectionTimer.Tick += OnConnectionTimerTick;
                _connectionTimer.Start();
                _fileLogger?.Info($"Started connection monitoring ({interval}ms interval)");
            }
        }

        private void StartHotkeyMonitoring()
        {
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

            _fileLogger?.Info("Stopped monitoring");
        }

        private void HotkeyPollingLoop(int intervalMs, CancellationToken token)
        {
            // Initialize SDL for hotkey detection
            bool sdlInitialized = false;
            try
            {
                sdlInitialized = SdlControllerWrapper.Initialize();
            }
            catch { }

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen || _popupShowing)
                    {
                        Thread.Sleep(intervalMs);
                        continue;
                    }

                    bool hotkeyPressed = false;
                    string controllerName = null;

                    // Check XInput controllers first
                    ushort xinputButtons = 0;
                    bool hasXInputController = false;

                    for (uint slot = 0; slot < 4; slot++)
                    {
                        XInputWrapper.XINPUT_STATE state = new XInputWrapper.XINPUT_STATE();
                        if (XInputWrapper.GetState(slot, ref state) == XInputWrapper.ERROR_SUCCESS)
                        {
                            hasXInputController = true;
                            xinputButtons |= state.Gamepad.wButtons;
                        }
                    }

                    if (hasXInputController)
                    {
                        hotkeyPressed = IsHotkeyPressed(xinputButtons, Settings.Settings.HotkeyCombo);
                        if (hotkeyPressed)
                        {
                            controllerName = XInputWrapper.GetControllerName();
                        }
                    }

                    // Check SDL if XInput didn't find the hotkey (for non-XInput controllers like DualSense)
                    // or if using Guide button hotkeys (XInput doesn't expose Guide)
                    if (!hotkeyPressed && sdlInitialized)
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

                    if (!hotkeyPressed)
                    {
                        _hotkeyWasTriggered = false;
                    }
                    else if (!_hotkeyWasTriggered)
                    {
                        _hotkeyWasTriggered = true;
                        _fileLogger?.Info($"Hotkey {Settings.Settings.HotkeyCombo} pressed (Controller: {controllerName})");

                        string finalName = controllerName;
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            TriggerFullscreenSwitch(FullscreenTriggerSource.Hotkey, finalName);
                        }));
                    }
                }
                catch (Exception ex)
                {
                    _fileLogger?.Error($"Hotkey polling error: {ex.Message}");
                }

                Thread.Sleep(intervalMs);
            }
        }

        private void OnConnectionTimerTick(object sender, EventArgs e)
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

        // For verbose debugging - track last logged state to avoid spam
        private static DateTime _lastVerboseLog = DateTime.MinValue;

        private void CheckControllerState()
        {
            var triggerMode = Settings.Settings.FullscreenTriggerMode;

            // Skip startup modes - they only check at startup
            if (triggerMode == FullscreenTriggerMode.Disabled ||
                triggerMode == FullscreenTriggerMode.XInputControllerOnStartup ||
                triggerMode == FullscreenTriggerMode.AnyControllerOnStartup)
            {
                return;
            }

            var state = GetControllerStateForMode(triggerMode);

            // Verbose logging every 5 seconds for debugging
            if (Settings.Settings.EnableLogging && (DateTime.Now - _lastVerboseLog).TotalSeconds >= 5)
            {
                _lastVerboseLog = DateTime.Now;
                _fileLogger?.Info($"[Poll] Connected={state.IsConnected}, Name={state.Name}, Source={state.Source}, WasConnected={_controllerWasConnected}");
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
                case ControllerHotkey.StartPlusRB:
                    return (buttons & XInputWrapper.XINPUT_GAMEPAD_START) != 0 &&
                           (buttons & XInputWrapper.XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0;
                case ControllerHotkey.StartPlusLB:
                    return (buttons & XInputWrapper.XINPUT_GAMEPAD_START) != 0 &&
                           (buttons & XInputWrapper.XINPUT_GAMEPAD_LEFT_SHOULDER) != 0;
                case ControllerHotkey.BackPlusStart:
                    return (buttons & XInputWrapper.XINPUT_GAMEPAD_BACK) != 0 &&
                           (buttons & XInputWrapper.XINPUT_GAMEPAD_START) != 0;
                case ControllerHotkey.BackPlusRB:
                    return (buttons & XInputWrapper.XINPUT_GAMEPAD_BACK) != 0 &&
                           (buttons & XInputWrapper.XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0;
                case ControllerHotkey.BackPlusLB:
                    return (buttons & XInputWrapper.XINPUT_GAMEPAD_BACK) != 0 &&
                           (buttons & XInputWrapper.XINPUT_GAMEPAD_LEFT_SHOULDER) != 0;
                // Guide button hotkeys - XInput doesn't expose Guide, always return false
                case ControllerHotkey.GuidePlusStart:
                case ControllerHotkey.GuidePlusBack:
                case ControllerHotkey.GuidePlusRB:
                case ControllerHotkey.GuidePlusLB:
                    return false;
                default:
                    return false;
            }
        }

        private bool IsSdlHotkeyPressed(SdlControllerWrapper.SdlButtons buttons, ControllerHotkey hotkey)
        {
            switch (hotkey)
            {
                case ControllerHotkey.StartPlusRB:
                    return (buttons & SdlControllerWrapper.SdlButtons.Start) != 0 &&
                           (buttons & SdlControllerWrapper.SdlButtons.RightShoulder) != 0;
                case ControllerHotkey.StartPlusLB:
                    return (buttons & SdlControllerWrapper.SdlButtons.Start) != 0 &&
                           (buttons & SdlControllerWrapper.SdlButtons.LeftShoulder) != 0;
                case ControllerHotkey.BackPlusStart:
                    return (buttons & SdlControllerWrapper.SdlButtons.Back) != 0 &&
                           (buttons & SdlControllerWrapper.SdlButtons.Start) != 0;
                case ControllerHotkey.BackPlusRB:
                    return (buttons & SdlControllerWrapper.SdlButtons.Back) != 0 &&
                           (buttons & SdlControllerWrapper.SdlButtons.RightShoulder) != 0;
                case ControllerHotkey.BackPlusLB:
                    return (buttons & SdlControllerWrapper.SdlButtons.Back) != 0 &&
                           (buttons & SdlControllerWrapper.SdlButtons.LeftShoulder) != 0;
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
                default:
                    return false;
            }
        }

        private void DelayedTrigger(int delayMs, Action action)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                action();
            };
            timer.Start();
        }

        private void TriggerFullscreenSwitch(FullscreenTriggerSource source, string controllerName)
        {
            _fileLogger?.Info($"TriggerFullscreenSwitch called: source={source}, controller={controllerName}");
            _popupShowing = true;

            try
            {
                bool skipPopup = source == FullscreenTriggerSource.Connection
                    ? Settings.Settings.SkipPopupOnConnection
                    : Settings.Settings.SkipPopupOnHotkey;

                if (skipPopup)
                {
                    _fileLogger?.Info($"Skipping popup (source: {source}), switching directly");
                    Application.Current.Dispatcher.Invoke(() => SwitchToFullscreen());
                }
                else
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var dialog = new ControllerDetectedDialog(Settings.Settings, source, controllerName);
                        var result = dialog.ShowDialog();

                        if (result == true && dialog.UserSelectedYes)
                        {
                            _fileLogger?.Info("User selected Yes - switching to fullscreen");
                            SwitchToFullscreen();
                        }
                        else
                        {
                            _fileLogger?.Info("User cancelled or dialog timed out");
                        }
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
                _fileLogger?.Info("Popup closed, _popupShowing = false");
            }
        }

        private void SwitchToFullscreen()
        {
            try
            {
                _fileLogger?.Info("Switching to fullscreen mode");

                var mainViewType = PlayniteApi.MainView.GetType();
                var switchMethod = mainViewType.GetMethod("SwitchToFullscreenMode",
                    BindingFlags.Instance | BindingFlags.Public);

                if (switchMethod != null)
                {
                    switchMethod.Invoke(PlayniteApi.MainView, null);
                }
                else
                {
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

        private void SendF11Key()
        {
            keybd_event(VK_F11, 0, 0, UIntPtr.Zero);
            Thread.Sleep(50);
            keybd_event(VK_F11, 0, 2, UIntPtr.Zero);
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

            bool isStartupMode = newTriggerMode == FullscreenTriggerMode.XInputControllerOnStartup ||
                                  newTriggerMode == FullscreenTriggerMode.AnyControllerOnStartup;

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
