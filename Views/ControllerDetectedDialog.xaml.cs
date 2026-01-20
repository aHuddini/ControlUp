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

        // Settings reference
        private readonly ControlUpSettings _settings;
        private readonly FullscreenTriggerSource _triggerSource;

        // Auto-close timer
        private DispatcherTimer _autoCloseTimer;
        private int _remainingSeconds;

        // XInput controller monitoring
        private CancellationTokenSource _controllerCts;
        private ushort _lastButtonState = 0;
        private int _selectedIndex = 0; // 0 = Yes, 1 = Cancel
        private bool _thumbstickWasCentered = true; // Track thumbstick state to prevent rapid switching
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
        public ControllerDetectedDialog() : this(null, FullscreenTriggerSource.Connection) { }

        // Constructor with settings
        public ControllerDetectedDialog(ControlUpSettings settings, FullscreenTriggerSource source = FullscreenTriggerSource.Connection)
        {
            _settings = settings ?? new ControlUpSettings();
            _triggerSource = source;
            _remainingSeconds = _settings.NotificationDurationSeconds;

            InitializeComponent();
            ApplySettings();
            ApplyTriggerSourceText();
        }

        private void ApplyTriggerSourceText()
        {
            if (_triggerSource == FullscreenTriggerSource.Hotkey)
            {
                TitleText.Text = "Hotkey Pressed";
                MessageText.Text = "Fullscreen mode hotkey was pressed.";
            }
            else
            {
                TitleText.Text = "Controller Detected";
                MessageText.Text = "A game controller has been connected.";
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
            // Position the window based on settings
            PositionWindow();

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

            // Initialize XInput button state
            InitializeControllerState();

            // Start controller input monitoring
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

        private void InitializeControllerState()
        {
            try
            {
                XInputWrapper.XINPUT_STATE state = new XInputWrapper.XINPUT_STATE();
                if (XInputWrapper.GetState(0, ref state) == 0)
                {
                    _lastButtonState = state.Gamepad.wButtons;
                }
            }
            catch { }
        }

        private void StartControllerMonitoring()
        {
            _controllerCts = new CancellationTokenSource();

            Task.Run(async () =>
            {
                await Task.Delay(100); // Initial delay to let window fully load

                while (!_controllerCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        XInputWrapper.XINPUT_STATE state = new XInputWrapper.XINPUT_STATE();
                        if (XInputWrapper.GetState(0, ref state) == 0)
                        {
                            ushort currentButtons = state.Gamepad.wButtons;
                            ushort pressedButtons = (ushort)(currentButtons & ~_lastButtonState);

                            // Check thumbstick position (left stick X axis, or right stick X axis)
                            short thumbLX = state.Gamepad.sThumbLX;
                            short thumbRX = state.Gamepad.sThumbRX;
                            bool thumbstickLeft = thumbLX < -THUMBSTICK_DEADZONE || thumbRX < -THUMBSTICK_DEADZONE;
                            bool thumbstickRight = thumbLX > THUMBSTICK_DEADZONE || thumbRX > THUMBSTICK_DEADZONE;
                            bool thumbstickCentered = !thumbstickLeft && !thumbstickRight;

                            // Handle thumbstick navigation (only trigger once per movement)
                            if (_thumbstickWasCentered && (thumbstickLeft || thumbstickRight))
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    _remainingSeconds = _settings.NotificationDurationSeconds;
                                    UpdateCountdown();

                                    _selectedIndex = _selectedIndex == 0 ? 1 : 0;
                                    UpdateButtonStyles();
                                    if (_selectedIndex == 0)
                                        YesButton.Focus();
                                    else
                                        CancelButton.Focus();
                                });
                                _thumbstickWasCentered = false;
                            }
                            else if (thumbstickCentered)
                            {
                                _thumbstickWasCentered = true;
                            }

                            if (pressedButtons != 0)
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    // Reset timer on any input
                                    _remainingSeconds = _settings.NotificationDurationSeconds;
                                    UpdateCountdown();

                                    // D-pad left/right to switch selection
                                    if ((pressedButtons & (XInputWrapper.XINPUT_GAMEPAD_DPAD_LEFT | XInputWrapper.XINPUT_GAMEPAD_DPAD_RIGHT)) != 0)
                                    {
                                        _selectedIndex = _selectedIndex == 0 ? 1 : 0;
                                        UpdateButtonStyles();
                                        if (_selectedIndex == 0)
                                            YesButton.Focus();
                                        else
                                            CancelButton.Focus();
                                    }
                                    // A button = confirm selection
                                    else if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_A) != 0)
                                    {
                                        if (_selectedIndex == 0)
                                            YesButton_Click(this, new RoutedEventArgs());
                                        else
                                            CancelButton_Click(this, new RoutedEventArgs());
                                    }
                                    // B button = cancel
                                    else if ((pressedButtons & XInputWrapper.XINPUT_GAMEPAD_B) != 0)
                                    {
                                        CancelButton_Click(this, new RoutedEventArgs());
                                    }
                                });
                            }

                            _lastButtonState = currentButtons;
                        }

                        await Task.Delay(50, _controllerCts.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                    catch { }
                }
            }, _controllerCts.Token);
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
            _remainingSeconds = _settings.NotificationDurationSeconds;
            UpdateCountdown();

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
                    _selectedIndex = _selectedIndex == 0 ? 1 : 0;
                    UpdateButtonStyles();
                    if (_selectedIndex == 0)
                        YesButton.Focus();
                    else
                        CancelButton.Focus();
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
            _autoCloseTimer?.Stop();
            _controllerCts?.Cancel();
            base.OnClosed(e);
        }
    }
}
