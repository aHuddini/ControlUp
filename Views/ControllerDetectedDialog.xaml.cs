using Playnite.SDK.Events;
using System;
using System.Runtime.InteropServices;
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

        // UI selection state
        private int _selectedIndex = 0; // 0 = Yes, 1 = Cancel

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

        /// <summary>
        /// Handles controller input forwarded from the main plugin via SDK events.
        /// Called on the UI thread by the main plugin's OnDesktopControllerButtonStateChanged handler.
        /// </summary>
        public void HandleControllerInput(ControllerInput button, ControllerInputState state)
        {
            // Only respond to button presses
            if (state != ControllerInputState.Pressed)
                return;

            ResetTimer();

            switch (button)
            {
                // D-pad and stick navigation
                case ControllerInput.DPadLeft:
                case ControllerInput.DPadRight:
                case ControllerInput.LeftStickLeft:
                case ControllerInput.LeftStickRight:
                case ControllerInput.RightStickLeft:
                case ControllerInput.RightStickRight:
                    SwitchSelection();
                    break;

                // A button = confirm selection
                case ControllerInput.A:
                    if (_selectedIndex == 0)
                        YesButton_Click(this, new RoutedEventArgs());
                    else
                        CancelButton_Click(this, new RoutedEventArgs());
                    break;

                // B button = cancel
                case ControllerInput.B:
                    CancelButton_Click(this, new RoutedEventArgs());
                    break;
            }
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
            UserSelectedYes = true;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _autoCloseTimer?.Stop();
            UserSelectedYes = false;
            DialogResult = false;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            Logger?.Info($"[Dialog] Closing dialog #{_dialogId}");

            // Stop and unhook auto-close timer
            if (_autoCloseTimer != null)
            {
                _autoCloseTimer.Stop();
                _autoCloseTimer.Tick -= AutoCloseTimer_Tick;
                _autoCloseTimer = null;
            }

            Logger?.Info($"[Dialog] Dialog #{_dialogId} cleanup complete");

            base.OnClosed(e);
        }
    }
}
