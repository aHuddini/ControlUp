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

        // Monitoring - background thread for hotkey (responsive), timer for connection detection
        private CancellationTokenSource _hotkeyCts;
        private Task _hotkeyTask;
        private DispatcherTimer _connectionTimer;

        // Controller state tracking
        private bool _xinputConnected = false;
        private bool _gamingInputConnected = false;

        // Prevent multiple popups
        private bool _popupShowing = false;
        private DateTime _lastPopupTime = DateTime.MinValue;
        private const int POPUP_COOLDOWN_SECONDS = 30;

        // Hotkey tracking
        private volatile bool _hotkeyWasTriggered = false;  // Prevents re-triggering while held

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
            _fileLogger?.Info($"Settings: TriggerMode={Settings.Settings.FullscreenTriggerMode}, Hotkey={Settings.Settings.EnableHotkey}, HotkeyCombo={Settings.Settings.HotkeyCombo}");

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

            // Check if we need monitoring at all
            bool needsConnectionMonitoring = Settings.Settings.FullscreenTriggerMode != FullscreenTriggerMode.Disabled;
            bool needsHotkeyMonitoring = Settings.Settings.EnableHotkey;

            if (!needsConnectionMonitoring && !needsHotkeyMonitoring)
            {
                _fileLogger?.Info("Both connection detection and hotkey disabled, monitoring not started");
                return;
            }

            // For "OnStartup" modes, show popup immediately if controller is already connected
            var triggerMode = Settings.Settings.FullscreenTriggerMode;
            if (triggerMode == FullscreenTriggerMode.UsbControllerOnStartup && _xinputConnected)
            {
                _fileLogger?.Info("USB controller detected on startup, triggering fullscreen");
                // Delay slightly to let Playnite fully load
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    TriggerFullscreenSwitch(FullscreenTriggerSource.Connection);
                };
                timer.Start();
                // Still start monitoring for hotkeys if enabled
                if (needsHotkeyMonitoring)
                {
                    StartMonitoring();
                }
                return;
            }
            else if (triggerMode == FullscreenTriggerMode.BluetoothControllerOnStartup && _gamingInputConnected)
            {
                _fileLogger?.Info("Bluetooth controller detected on startup, triggering fullscreen");
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    TriggerFullscreenSwitch(FullscreenTriggerSource.Connection);
                };
                timer.Start();
                // Still start monitoring for hotkeys if enabled
                if (needsHotkeyMonitoring)
                {
                    StartMonitoring();
                }
                return;
            }

            // Start monitoring for connections and/or hotkeys
            if (needsConnectionMonitoring || needsHotkeyMonitoring)
            {
                StartMonitoring();
            }
            else
            {
                _fileLogger?.Info("Startup mode but no controller detected at startup, hotkeys disabled");
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
            // Background thread for hotkey detection - much more responsive than DispatcherTimer
            if (Settings.Settings.EnableHotkey)
            {
                var interval = Settings.Settings.HotkeyPollingIntervalMs;
                _hotkeyCts = new CancellationTokenSource();
                _hotkeyTask = Task.Run(() => HotkeyPollingLoop(interval, _hotkeyCts.Token));
                _fileLogger?.Info($"Started hotkey monitoring on background thread ({interval}ms interval)");
            }

            // Slower timer for connection detection (500ms = sufficient for detecting plugs)
            var triggerMode = Settings.Settings.FullscreenTriggerMode;
            bool needsConnectionMonitoring = triggerMode == FullscreenTriggerMode.UsbControllerConnected ||
                                              triggerMode == FullscreenTriggerMode.BluetoothControllerConnected ||
                                              triggerMode == FullscreenTriggerMode.AnyControllerConnected;

            if (needsConnectionMonitoring)
            {
                _connectionTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                _connectionTimer.Tick += OnConnectionTimerTick;
                _connectionTimer.Start();
                _fileLogger?.Info("Started connection monitoring (500ms interval)");
            }

            Logger.Info("ControlUp: Started controller monitoring");
        }

        private void StopMonitoring()
        {
            // Stop hotkey background thread
            if (_hotkeyCts != null)
            {
                _hotkeyCts.Cancel();
                try
                {
                    _hotkeyTask?.Wait(500);  // Wait up to 500ms for clean shutdown
                }
                catch { }
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

            Logger.Info("ControlUp: Stopped monitoring");
            _fileLogger?.Info("Stopped monitoring");
        }

        private void HotkeyPollingLoop(int intervalMs, CancellationToken token)
        {
            uint lastPacketNumber = 0;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Don't check if already in fullscreen or popup is showing
                    if (PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen ||
                        _popupShowing)
                    {
                        Thread.Sleep(intervalMs);
                        continue;
                    }

                    // Check all controller slots (0-3) for input
                    ushort buttons = 0;
                    bool hasController = false;
                    uint currentPacket = 0;

                    for (uint slot = 0; slot < 4; slot++)
                    {
                        XInputWrapper.XINPUT_STATE state = new XInputWrapper.XINPUT_STATE();
                        if (XInputWrapper.GetState(slot, ref state) == XInputWrapper.ERROR_SUCCESS)
                        {
                            hasController = true;
                            buttons |= state.Gamepad.wButtons;
                            currentPacket = Math.Max(currentPacket, state.dwPacketNumber);
                        }
                    }

                    // Skip if no controller or no state change (optimization)
                    if (!hasController)
                    {
                        _hotkeyWasTriggered = false;
                        Thread.Sleep(intervalMs);
                        continue;
                    }

                    // Check hotkey combo
                    bool hotkeyPressed = IsHotkeyPressed(buttons, Settings.Settings.HotkeyCombo);

                    // Reset trigger flag when combo is released
                    if (!hotkeyPressed)
                    {
                        _hotkeyWasTriggered = false;
                    }
                    // Trigger when combo is pressed and hasn't been triggered yet
                    else if (hotkeyPressed && !_hotkeyWasTriggered)
                    {
                        _hotkeyWasTriggered = true;
                        _fileLogger?.Info($"Hotkey {Settings.Settings.HotkeyCombo} pressed, triggering fullscreen");

                        // Dispatch to UI thread
                        Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            TriggerFullscreenSwitch(FullscreenTriggerSource.Hotkey);
                        }));
                    }

                    lastPacketNumber = currentPacket;
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
            // Don't monitor if already in fullscreen
            if (PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen)
                return;

            // Don't check connection state if popup is showing or we're in cooldown
            if (_popupShowing)
                return;

            if ((DateTime.Now - _lastPopupTime).TotalSeconds < POPUP_COOLDOWN_SECONDS)
                return;

            CheckControllerState();
        }

        private void CheckControllerState()
        {
            var triggerMode = Settings.Settings.FullscreenTriggerMode;

            // Only runtime connection modes should trigger here
            // Startup modes (UsbControllerOnStartup, BluetoothControllerOnStartup) are handled
            // only at application start and should NOT respond to runtime connections
            if (triggerMode == FullscreenTriggerMode.Disabled ||
                triggerMode == FullscreenTriggerMode.UsbControllerOnStartup ||
                triggerMode == FullscreenTriggerMode.BluetoothControllerOnStartup)
            {
                return;
            }

            bool xinputNow = XInputWrapper.IsControllerConnected();
            bool gamingInputNow = GamingInputWrapper.IsControllerConnected();

            bool shouldShowPopup = false;
            string controllerType = "";

            // Check for NEW connections based on trigger mode (runtime modes only)
            switch (triggerMode)
            {
                case FullscreenTriggerMode.UsbControllerConnected:
                    // USB/XInput controller newly connected
                    if (xinputNow && !_xinputConnected)
                    {
                        shouldShowPopup = true;
                        controllerType = "USB/XInput";
                    }
                    break;

                case FullscreenTriggerMode.BluetoothControllerConnected:
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

            // Trigger fullscreen switch if needed
            if (shouldShowPopup)
            {
                _fileLogger?.Info($"New {controllerType} controller detected, triggering fullscreen");
                TriggerFullscreenSwitch(FullscreenTriggerSource.Connection);
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
                default:
                    return false;
            }
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
                    // Show popup
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var dialog = new ControllerDetectedDialog(Settings.Settings, source);
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
            }
            catch (Exception ex)
            {
                _fileLogger?.Error($"Error triggering fullscreen: {ex.Message}");
                Logger.Error(ex, "ControlUp: Error triggering fullscreen switch");
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
