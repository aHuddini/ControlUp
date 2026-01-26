using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ControlUp.Common;

namespace ControlUp.Dialogs
{
    public partial class ControllerDetectedDialog : Window
    {
        public bool UserSelectedYes { get; private set; } = false;

        // Static logger and dialog counter for diagnostics
        public static FileLogger Logger { get; set; }
        private static int _dialogCounter = 0;
        private readonly int _dialogId;

        // Settings reference
        private readonly ControlUpSettings _settings;
        private readonly FullscreenTriggerSource _triggerSource;
        private readonly string _controllerName;

        // Auto-close timer
        private DispatcherTimer _autoCloseTimer;
        private int _remainingSeconds;

        // SDL2 controller monitoring
        private CancellationTokenSource _controllerCts;
        private SdlControllerWrapper.SdlButtons _lastButtons = SdlControllerWrapper.SdlButtons.None;
        private int _selectedIndex = 0; // 0 = Yes, 1 = Cancel
        private bool _thumbstickWasCentered = true;
        private const short THUMBSTICK_DEADZONE = 16000; // ~50% of max range

        // Windows Composition API for blur
        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        private enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19
        }

        private enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
            ACCENT_INVALID_STATE = 5
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public AccentState AccentState;
            public uint AccentFlags;
            public uint GradientColor; // Format: AABBGGRR
            public int AnimationId;
        }

        // Constructor with default settings
        public ControllerDetectedDialog() : this(null, FullscreenTriggerSource.Connection, null) { }

        // Constructor with settings
        public ControllerDetectedDialog(ControlUpSettings settings, FullscreenTriggerSource source, string controllerName = null)
        {
            _dialogCounter++;
            _dialogId = _dialogCounter;
            Logger?.Info($"[Dialog] Creating dialog #{_dialogId} (source={source}, controller={controllerName})");

            _settings = settings ?? new ControlUpSettings();
            _triggerSource = source;
            _controllerName = controllerName;
            _remainingSeconds = _settings.NotificationDurationSeconds;

            InitializeComponent();
            ApplySettings();
            ApplyTriggerSourceText();

            // Position window before showing to avoid flicker
            PositionWindow();
        }

        private void ApplyTriggerSourceText()
        {
            if (_triggerSource == FullscreenTriggerSource.Hotkey)
            {
                TitleText.Text = "Hotkey Pressed";
                MessageText.Text = !string.IsNullOrEmpty(_controllerName)
                    ? $"{_controllerName} hotkey was pressed."
                    : "Fullscreen mode hotkey was pressed.";
            }
            else
            {
                TitleText.Text = "Controller Detected";
                MessageText.Text = !string.IsNullOrEmpty(_controllerName)
                    ? $"{_controllerName} has been connected."
                    : "A game controller has been connected.";
            }
        }

        private void ApplySettings()
        {
            // Apply size
            Width = _settings.NotificationWidth;
            Height = _settings.NotificationHeight;

            // Apply visual settings to the main border
            var bgColor = HexToColor(_settings.BackgroundColor);
            var borderColor = HexToColor(_settings.BorderColor);

            MainBorder.Background = new SolidColorBrush(Color.FromArgb(
                (byte)_settings.BackgroundOpacity, bgColor.R, bgColor.G, bgColor.B));
            MainBorder.BorderBrush = new SolidColorBrush(borderColor);
            MainBorder.BorderThickness = new Thickness(_settings.BorderThickness);
            MainBorder.CornerRadius = new CornerRadius(_settings.CornerRadius);

            // Update countdown text
            CountdownText.Text = $"Auto-closing in {_remainingSeconds}s...";
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Focus the Yes button
            YesButton.Focus();
            UpdateButtonStyles();

            // Start auto-close timer
            _autoCloseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _autoCloseTimer.Tick += AutoCloseTimer_Tick;
            _autoCloseTimer.Start();

            // Initialize SDL and start controller monitoring
            StartControllerMonitoring();
        }

        private void PositionWindow()
        {
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            var margin = _settings.NotificationEdgeMargin;

            switch (_settings.NotificationPosition)
            {
                case NotificationPosition.TopLeft:
                    Left = margin;
                    Top = margin;
                    break;
                case NotificationPosition.TopCenter:
                    Left = (screenWidth - Width) / 2;
                    Top = margin;
                    break;
                case NotificationPosition.TopRight:
                    Left = screenWidth - Width - margin;
                    Top = margin;
                    break;
                case NotificationPosition.Center:
                    Left = (screenWidth - Width) / 2;
                    Top = (screenHeight - Height) / 2;
                    break;
                case NotificationPosition.BottomLeft:
                    Left = margin;
                    Top = screenHeight - Height - margin;
                    break;
                case NotificationPosition.BottomCenter:
                    Left = (screenWidth - Width) / 2;
                    Top = screenHeight - Height - margin;
                    break;
                case NotificationPosition.BottomRight:
                    Left = screenWidth - Width - margin;
                    Top = screenHeight - Height - margin;
                    break;
            }
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            // Apply blur effect after window handle is created
            if (_settings.EnableBlur)
            {
                EnableAcrylicBlur();
            }
        }

        private void EnableAcrylicBlur()
        {
            try
            {
                var windowHelper = new WindowInteropHelper(this);
                var hwnd = windowHelper.EnsureHandle();

                // Parse tint color from settings
                var tintColor = HexToColor(_settings.BlurTintColor);

                // GradientColor format is AABBGGRR
                uint gradientColor = ((uint)_settings.BlurOpacity << 24) |
                                    ((uint)tintColor.B << 16) |
                                    ((uint)tintColor.G << 8) |
                                    tintColor.R;

                var blurState = _settings.BlurMode == 0
                    ? AccentState.ACCENT_ENABLE_BLURBEHIND
                    : AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND;

                var accent = new AccentPolicy
                {
                    AccentState = blurState,
                    AccentFlags = 0,
                    GradientColor = gradientColor,
                    AnimationId = 0
                };

                var accentSize = Marshal.SizeOf(accent);
                var accentPtr = Marshal.AllocHGlobal(accentSize);

                try
                {
                    Marshal.StructureToPtr(accent, accentPtr, false);

                    var data = new WindowCompositionAttributeData
                    {
                        Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                        Data = accentPtr,
                        SizeOfData = accentSize
                    };

                    int result = SetWindowCompositionAttribute(hwnd, ref data);

                    // Fallback to basic blur if acrylic fails
                    if (result == 0 && blurState == AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND)
                    {
                        accent.AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND;
                        Marshal.StructureToPtr(accent, accentPtr, false);
                        SetWindowCompositionAttribute(hwnd, ref data);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(accentPtr);
                }
            }
            catch
            {
                // Blur not supported, continue without it
            }
        }

        private Color HexToColor(string hex)
        {
            try
            {
                hex = hex.TrimStart('#');
                if (hex.Length == 6)
                {
                    return Color.FromRgb(
                        Convert.ToByte(hex.Substring(0, 2), 16),
                        Convert.ToByte(hex.Substring(2, 2), 16),
                        Convert.ToByte(hex.Substring(4, 2), 16));
                }
            }
            catch { }
            return Color.FromRgb(0x1E, 0x1E, 0x1E); // Default dark gray
        }

        private void StartControllerMonitoring()
        {
            _controllerCts = new CancellationTokenSource();

            // Initialize SDL and open controller
            if (!SdlControllerWrapper.Initialize())
            {
                Logger?.Error($"[Dialog] Failed to initialize SDL");
                return;
            }

            if (!SdlControllerWrapper.OpenController())
            {
                Logger?.Info($"[Dialog] No SDL controller found, dialog will use keyboard only");
                return;
            }

            Logger?.Info($"[Dialog] SDL controller opened for dialog navigation");

            // Get initial button state to avoid triggering on already-pressed buttons
            var initialReading = SdlControllerWrapper.GetCurrentReading();
            if (initialReading.IsValid)
            {
                _lastButtons = initialReading.Buttons;
            }

            Task.Run(async () =>
            {
                await Task.Delay(100); // Initial delay to let window fully load

                while (!_controllerCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        PollSdlController();
                        await Task.Delay(50, _controllerCts.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Logger?.Error($"[Dialog] SDL poll error: {ex.Message}");
                    }
                }
            }, _controllerCts.Token);
        }

        private void PollSdlController()
        {
            var reading = SdlControllerWrapper.GetCurrentReading();
            if (!reading.IsValid) return;

            var currentButtons = reading.Buttons;
            var pressedButtons = currentButtons & ~_lastButtons; // Newly pressed

            // Check thumbstick for navigation
            bool thumbstickLeft = reading.LeftStickX < -THUMBSTICK_DEADZONE || reading.RightStickX < -THUMBSTICK_DEADZONE;
            bool thumbstickRight = reading.LeftStickX > THUMBSTICK_DEADZONE || reading.RightStickX > THUMBSTICK_DEADZONE;
            bool thumbstickCentered = !thumbstickLeft && !thumbstickRight;

            // Handle thumbstick navigation (only trigger once per movement)
            if (_thumbstickWasCentered && (thumbstickLeft || thumbstickRight))
            {
                Dispatcher.Invoke(() =>
                {
                    ResetTimer();
                    SwitchSelection();
                });
                _thumbstickWasCentered = false;
            }
            else if (thumbstickCentered)
            {
                _thumbstickWasCentered = true;
            }

            // Handle button presses
            if (pressedButtons != SdlControllerWrapper.SdlButtons.None)
            {
                Dispatcher.Invoke(() =>
                {
                    ResetTimer();

                    // D-pad left/right to switch selection
                    if ((pressedButtons & (SdlControllerWrapper.SdlButtons.DPadLeft | SdlControllerWrapper.SdlButtons.DPadRight)) != 0)
                    {
                        SwitchSelection();
                    }
                    // A button = confirm selection
                    else if ((pressedButtons & SdlControllerWrapper.SdlButtons.A) != 0)
                    {
                        if (_selectedIndex == 0)
                            YesButton_Click(this, new RoutedEventArgs());
                        else
                            CancelButton_Click(this, new RoutedEventArgs());
                    }
                    // B button = cancel
                    else if ((pressedButtons & SdlControllerWrapper.SdlButtons.B) != 0)
                    {
                        CancelButton_Click(this, new RoutedEventArgs());
                    }
                });
            }

            _lastButtons = currentButtons;
        }

        private void ResetTimer()
        {
            _remainingSeconds = _settings.NotificationDurationSeconds;
            UpdateCountdown();
        }

        private void SwitchSelection()
        {
            _selectedIndex = _selectedIndex == 0 ? 1 : 0;
            UpdateButtonStyles();
            if (_selectedIndex == 0)
                YesButton.Focus();
            else
                CancelButton.Focus();
        }

        private void UpdateButtonStyles()
        {
            // Yes button styling
            if (_selectedIndex == 0)
            {
                YesButton.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                YesButton.BorderBrush = new SolidColorBrush(Colors.White);
                YesButton.BorderThickness = new Thickness(3);
            }
            else
            {
                YesButton.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));
                YesButton.BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100));
                YesButton.BorderThickness = new Thickness(1);
            }

            // Cancel button styling
            if (_selectedIndex == 1)
            {
                CancelButton.Background = new SolidColorBrush(Color.FromRgb(97, 97, 97)); // Gray
                CancelButton.BorderBrush = new SolidColorBrush(Colors.White);
                CancelButton.BorderThickness = new Thickness(3);
            }
            else
            {
                CancelButton.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));
                CancelButton.BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100));
                CancelButton.BorderThickness = new Thickness(1);
            }
        }

        private void AutoCloseTimer_Tick(object sender, EventArgs e)
        {
            _remainingSeconds--;
            UpdateCountdown();

            if (_remainingSeconds <= 0)
            {
                _autoCloseTimer.Stop();
                UserSelectedYes = false;
                DialogResult = false;
                Close();
            }
        }

        private void UpdateCountdown()
        {
            CountdownText.Text = $"Auto-closing in {_remainingSeconds}s...";
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Reset timer on any input
            ResetTimer();

            switch (e.Key)
            {
                case Key.Enter:
                case Key.Space:
                    if (_selectedIndex == 0)
                        YesButton_Click(sender, e);
                    else
                        CancelButton_Click(sender, e);
                    e.Handled = true;
                    break;

                case Key.Escape:
                    CancelButton_Click(sender, e);
                    e.Handled = true;
                    break;

                case Key.Left:
                case Key.Right:
                    SwitchSelection();
                    e.Handled = true;
                    break;
            }
        }

        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            _autoCloseTimer?.Stop();
            _controllerCts?.Cancel();
            UserSelectedYes = true;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _autoCloseTimer?.Stop();
            _controllerCts?.Cancel();
            UserSelectedYes = false;
            DialogResult = false;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            Logger?.Info($"[Dialog] Closing dialog #{_dialogId} - starting cleanup");

            // Stop and unhook auto-close timer
            if (_autoCloseTimer != null)
            {
                _autoCloseTimer.Stop();
                _autoCloseTimer.Tick -= AutoCloseTimer_Tick;
                _autoCloseTimer = null;
            }

            // Cancel controller monitoring and wait for it to stop
            if (_controllerCts != null)
            {
                _controllerCts.Cancel();
                Thread.Sleep(100); // Give the polling loop time to exit
                _controllerCts.Dispose();
                _controllerCts = null;
            }

            // Close SDL controller handle but DON'T shutdown SDL
            // SDL_Quit corrupts COM apartment state causing ITfThreadMgr cast failures
            SdlControllerWrapper.CloseController();
            Logger?.Info($"[Dialog] SDL controller closed (SDL stays initialized)");

            Logger?.Info($"[Dialog] Dialog #{_dialogId} cleanup complete");

            base.OnClosed(e);
        }
    }
}
