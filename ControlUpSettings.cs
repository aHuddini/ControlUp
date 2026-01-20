using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace ControlUp
{
    public class ControlUpSettings : ObservableObject, ISettings
    {
        // === General Settings ===
        private FullscreenTriggerMode _fullscreenTriggerMode = FullscreenTriggerMode.UsbControllerConnected;
        private bool _enableLogging = false;
        private bool _skipPopupOnConnection = false;

        public FullscreenTriggerMode FullscreenTriggerMode
        {
            get => _fullscreenTriggerMode;
            set => SetValue(ref _fullscreenTriggerMode, value);
        }

        public bool EnableLogging
        {
            get => _enableLogging;
            set => SetValue(ref _enableLogging, value);
        }

        /// <summary>
        /// Skip popup and go straight to fullscreen when controller is connected
        /// </summary>
        public bool SkipPopupOnConnection
        {
            get => _skipPopupOnConnection;
            set => SetValue(ref _skipPopupOnConnection, value);
        }

        // === Hotkey Settings ===
        private bool _enableHotkey = true;
        private ControllerHotkey _hotkeyCombo = ControllerHotkey.StartPlusRB;
        private bool _skipPopupOnHotkey = false;

        /// <summary>
        /// Enable controller hotkey to trigger fullscreen switch
        /// </summary>
        public bool EnableHotkey
        {
            get => _enableHotkey;
            set => SetValue(ref _enableHotkey, value);
        }

        /// <summary>
        /// Controller button combination for hotkey
        /// </summary>
        public ControllerHotkey HotkeyCombo
        {
            get => _hotkeyCombo;
            set => SetValue(ref _hotkeyCombo, value);
        }

        /// <summary>
        /// Skip popup and go straight to fullscreen when hotkey is pressed
        /// </summary>
        public bool SkipPopupOnHotkey
        {
            get => _skipPopupOnHotkey;
            set => SetValue(ref _skipPopupOnHotkey, value);
        }

        private int _hotkeyPollingIntervalMs = 70;

        /// <summary>
        /// Hotkey polling interval in milliseconds (5-500).
        /// Lower = more responsive but uses more CPU. Default 70ms. Recommended: 50-100ms.
        /// </summary>
        public int HotkeyPollingIntervalMs
        {
            get => _hotkeyPollingIntervalMs;
            set => SetValue(ref _hotkeyPollingIntervalMs, Math.Max(5, Math.Min(500, value)));
        }

        // === Notification Settings ===
        private NotificationPosition _notificationPosition = NotificationPosition.TopCenter;
        private int _notificationDurationSeconds = 20;
        private int _notificationWidth = 574;
        private int _notificationHeight = 320;
        private int _notificationEdgeMargin = 4;

        // Blur settings
        private bool _enableBlur = true;
        private int _blurOpacity = 49;  // 0-255
        private string _blurTintColor = "00106C";  // Hex RGB without #
        private int _blurMode = 1;  // 0 = Basic blur, 1 = Acrylic blur

        // Visual settings
        private string _backgroundColor = "071134";  // Hex RGB without #
        private int _backgroundOpacity = 138;  // 0-255
        private string _borderColor = "313553";  // Hex RGB without #
        private int _borderThickness = 1;
        private int _cornerRadius = 0;

        /// <summary>
        /// Position of the notification on screen
        /// </summary>
        public NotificationPosition NotificationPosition
        {
            get => _notificationPosition;
            set => SetValue(ref _notificationPosition, value);
        }

        /// <summary>
        /// How long the notification displays before auto-closing (5-30 seconds)
        /// </summary>
        public int NotificationDurationSeconds
        {
            get => _notificationDurationSeconds;
            set => SetValue(ref _notificationDurationSeconds, Math.Max(5, Math.Min(30, value)));
        }

        /// <summary>
        /// Notification width in pixels (300-800)
        /// </summary>
        public int NotificationWidth
        {
            get => _notificationWidth;
            set => SetValue(ref _notificationWidth, Math.Max(300, Math.Min(800, value)));
        }

        /// <summary>
        /// Notification height in pixels (200-500)
        /// </summary>
        public int NotificationHeight
        {
            get => _notificationHeight;
            set => SetValue(ref _notificationHeight, Math.Max(200, Math.Min(500, value)));
        }

        /// <summary>
        /// Margin from screen edge in pixels (0-100)
        /// </summary>
        public int NotificationEdgeMargin
        {
            get => _notificationEdgeMargin;
            set => SetValue(ref _notificationEdgeMargin, Math.Max(0, Math.Min(100, value)));
        }

        /// <summary>
        /// Enable acrylic/blur effect on notification
        /// </summary>
        public bool EnableBlur
        {
            get => _enableBlur;
            set => SetValue(ref _enableBlur, value);
        }

        /// <summary>
        /// Blur opacity (0-255). Higher = more opaque
        /// </summary>
        public int BlurOpacity
        {
            get => _blurOpacity;
            set => SetValue(ref _blurOpacity, Math.Max(0, Math.Min(255, value)));
        }

        /// <summary>
        /// Blur tint color (RGB hex without #)
        /// </summary>
        public string BlurTintColor
        {
            get => _blurTintColor;
            set => SetValue(ref _blurTintColor, value ?? "00106C");
        }

        /// <summary>
        /// Blur mode: 0 = Basic blur, 1 = Acrylic blur (with noise texture)
        /// </summary>
        public int BlurMode
        {
            get => _blurMode;
            set => SetValue(ref _blurMode, Math.Max(0, Math.Min(1, value)));
        }

        /// <summary>
        /// Background color (RGB hex without #)
        /// </summary>
        public string BackgroundColor
        {
            get => _backgroundColor;
            set => SetValue(ref _backgroundColor, value ?? "071134");
        }

        /// <summary>
        /// Background opacity (0-255). Lower = more transparent to show blur
        /// </summary>
        public int BackgroundOpacity
        {
            get => _backgroundOpacity;
            set => SetValue(ref _backgroundOpacity, Math.Max(0, Math.Min(255, value)));
        }

        /// <summary>
        /// Border color (RGB hex without #)
        /// </summary>
        public string BorderColor
        {
            get => _borderColor;
            set => SetValue(ref _borderColor, value ?? "313553");
        }

        /// <summary>
        /// Border thickness in pixels (0-5)
        /// </summary>
        public int BorderThickness
        {
            get => _borderThickness;
            set => SetValue(ref _borderThickness, Math.Max(0, Math.Min(5, value)));
        }

        /// <summary>
        /// Corner radius in pixels (0-32)
        /// </summary>
        public int CornerRadius
        {
            get => _cornerRadius;
            set => SetValue(ref _cornerRadius, Math.Max(0, Math.Min(32, value)));
        }

        public void BeginEdit()
        {
            // No special edit handling needed
        }

        public void CancelEdit()
        {
            // No special cancel handling needed
        }

        public void EndEdit()
        {
            // No special end edit handling needed
        }

        public bool IsEqual(ISettings other)
        {
            if (other is ControlUpSettings otherSettings)
            {
                return FullscreenTriggerMode == otherSettings.FullscreenTriggerMode &&
                       EnableLogging == otherSettings.EnableLogging;
            }
            return false;
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            return true;
        }
    }

    public enum FullscreenTriggerMode
    {
        [Description("Disabled - No controller detection")]
        Disabled,

        [Description("USB Controller - Detect USB/wired Xbox controllers")]
        UsbControllerConnected,

        [Description("Bluetooth Controller - Detect Bluetooth wireless controllers")]
        BluetoothControllerConnected,

        [Description("Any Controller - Detect any controller type")]
        AnyControllerConnected,

        // Legacy modes kept for compatibility
        [Description("USB Controller (On Startup)")]
        UsbControllerOnStartup,

        [Description("Bluetooth Controller (On Startup)")]
        BluetoothControllerOnStartup
    }

    public enum NotificationPosition
    {
        [Description("Top Left")]
        TopLeft,

        [Description("Top Center")]
        TopCenter,

        [Description("Top Right")]
        TopRight,

        [Description("Center")]
        Center,

        [Description("Bottom Left")]
        BottomLeft,

        [Description("Bottom Center")]
        BottomCenter,

        [Description("Bottom Right")]
        BottomRight
    }

    public enum ControllerHotkey
    {
        [Description("Start + RB")]
        StartPlusRB,

        [Description("Start + LB")]
        StartPlusLB,

        [Description("Back + Start")]
        BackPlusStart,

        [Description("Back + RB")]
        BackPlusRB,

        [Description("Back + LB")]
        BackPlusLB
    }

    /// <summary>
    /// Identifies what triggered the fullscreen switch dialog
    /// </summary>
    public enum FullscreenTriggerSource
    {
        /// <summary>
        /// Triggered by a new controller being connected
        /// </summary>
        Connection,

        /// <summary>
        /// Triggered by the user pressing the controller hotkey
        /// </summary>
        Hotkey
    }
}
