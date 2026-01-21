using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace ControlUp
{
    public class ControlUpSettings : ObservableObject, ISettings
    {
        // General Settings
        private FullscreenTriggerMode _fullscreenTriggerMode = FullscreenTriggerMode.XInputController;
        private bool _enableLogging = false;
        private bool _skipPopupOnConnection = false;

        public FullscreenTriggerMode FullscreenTriggerMode
        {
            get => _fullscreenTriggerMode;
            set => SetValue(ref _fullscreenTriggerMode, MigrateLegacyMode(value));
        }

        // Migrate legacy enum values to new simplified modes
        private static FullscreenTriggerMode MigrateLegacyMode(FullscreenTriggerMode mode)
        {
            switch (mode)
            {
                case FullscreenTriggerMode.UsbControllerConnected:
                case FullscreenTriggerMode.BluetoothControllerConnected:
                case FullscreenTriggerMode.AnyControllerConnected:
                    return FullscreenTriggerMode.XInputController;
                case FullscreenTriggerMode.UsbControllerOnStartup:
                case FullscreenTriggerMode.BluetoothControllerOnStartup:
                    return FullscreenTriggerMode.XInputControllerOnStartup;
                default:
                    return mode;
            }
        }

        public bool EnableLogging
        {
            get => _enableLogging;
            set => SetValue(ref _enableLogging, value);
        }

        public bool SkipPopupOnConnection
        {
            get => _skipPopupOnConnection;
            set => SetValue(ref _skipPopupOnConnection, value);
        }

        // Hotkey Settings
        private bool _enableHotkey = true;
        private ControllerHotkey _hotkeyCombo = ControllerHotkey.StartPlusRB;
        private bool _skipPopupOnHotkey = false;
        private int _hotkeyPollingIntervalMs = 70;

        public bool EnableHotkey
        {
            get => _enableHotkey;
            set => SetValue(ref _enableHotkey, value);
        }

        public ControllerHotkey HotkeyCombo
        {
            get => _hotkeyCombo;
            set => SetValue(ref _hotkeyCombo, value);
        }

        public bool SkipPopupOnHotkey
        {
            get => _skipPopupOnHotkey;
            set => SetValue(ref _skipPopupOnHotkey, value);
        }

        /// <summary>Polling interval in ms (5-500). Lower = more responsive. Default: 70ms.</summary>
        public int HotkeyPollingIntervalMs
        {
            get => _hotkeyPollingIntervalMs;
            set => SetValue(ref _hotkeyPollingIntervalMs, Math.Max(5, Math.Min(500, value)));
        }

        // Notification Settings
        private NotificationPosition _notificationPosition = NotificationPosition.TopCenter;
        private int _notificationDurationSeconds = 20;
        private int _notificationWidth = 574;
        private int _notificationHeight = 320;
        private int _notificationEdgeMargin = 4;

        // Blur settings
        private bool _enableBlur = true;
        private int _blurOpacity = 49;
        private string _blurTintColor = "00106C";
        private int _blurMode = 1;  // 0 = Basic, 1 = Acrylic

        // Visual settings
        private string _backgroundColor = "071134";
        private int _backgroundOpacity = 138;
        private string _borderColor = "313553";
        private int _borderThickness = 1;
        private int _cornerRadius = 0;

        public NotificationPosition NotificationPosition
        {
            get => _notificationPosition;
            set => SetValue(ref _notificationPosition, value);
        }

        public int NotificationDurationSeconds
        {
            get => _notificationDurationSeconds;
            set => SetValue(ref _notificationDurationSeconds, Math.Max(5, Math.Min(30, value)));
        }

        public int NotificationWidth
        {
            get => _notificationWidth;
            set => SetValue(ref _notificationWidth, Math.Max(300, Math.Min(800, value)));
        }

        public int NotificationHeight
        {
            get => _notificationHeight;
            set => SetValue(ref _notificationHeight, Math.Max(200, Math.Min(500, value)));
        }

        public int NotificationEdgeMargin
        {
            get => _notificationEdgeMargin;
            set => SetValue(ref _notificationEdgeMargin, Math.Max(0, Math.Min(100, value)));
        }

        public bool EnableBlur
        {
            get => _enableBlur;
            set => SetValue(ref _enableBlur, value);
        }

        public int BlurOpacity
        {
            get => _blurOpacity;
            set => SetValue(ref _blurOpacity, Math.Max(0, Math.Min(255, value)));
        }

        /// <summary>RGB hex without # prefix.</summary>
        public string BlurTintColor
        {
            get => _blurTintColor;
            set => SetValue(ref _blurTintColor, value ?? "00106C");
        }

        /// <summary>0 = Basic blur, 1 = Acrylic blur.</summary>
        public int BlurMode
        {
            get => _blurMode;
            set => SetValue(ref _blurMode, Math.Max(0, Math.Min(1, value)));
        }

        /// <summary>RGB hex without # prefix.</summary>
        public string BackgroundColor
        {
            get => _backgroundColor;
            set => SetValue(ref _backgroundColor, value ?? "071134");
        }

        public int BackgroundOpacity
        {
            get => _backgroundOpacity;
            set => SetValue(ref _backgroundOpacity, Math.Max(0, Math.Min(255, value)));
        }

        /// <summary>RGB hex without # prefix.</summary>
        public string BorderColor
        {
            get => _borderColor;
            set => SetValue(ref _borderColor, value ?? "313553");
        }

        public int BorderThickness
        {
            get => _borderThickness;
            set => SetValue(ref _borderThickness, Math.Max(0, Math.Min(5, value)));
        }

        public int CornerRadius
        {
            get => _cornerRadius;
            set => SetValue(ref _cornerRadius, Math.Max(0, Math.Min(32, value)));
        }

        public void BeginEdit() { }
        public void CancelEdit() { }
        public void EndEdit() { }

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

        [Description("[On Startup Only] XInput - XInput-compatible controllers")]
        XInputControllerOnStartup,

        [Description("[On Startup Only] Any Controller - XInput or other non-XInput controllers")]
        AnyControllerOnStartup,

        [Description("XInput Connected - XInput-compatible controllers")]
        XInputController,

        [Description("Any Controller Connected - XInput or other non-XInput controllers")]
        AnyController,

        // Legacy values kept for settings migration (hidden from UI)
        [Description("")]
        UsbControllerConnected = 10,
        [Description("")]
        BluetoothControllerConnected = 11,
        [Description("")]
        AnyControllerConnected = 12,
        [Description("")]
        UsbControllerOnStartup = 13,
        [Description("")]
        BluetoothControllerOnStartup = 14
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
        [Description("Start + RB (Options + R1)")]
        StartPlusRB,

        [Description("Start + LB (Options + L1)")]
        StartPlusLB,

        [Description("Back + Start (Share + Options)")]
        BackPlusStart,

        [Description("Back + RB (Share + R1)")]
        BackPlusRB,

        [Description("Back + LB (Share + L1)")]
        BackPlusLB,

        [Description("Guide + Start (PS + Options)")]
        GuidePlusStart,

        [Description("Guide + Back (PS + Share)")]
        GuidePlusBack,

        [Description("Guide + RB (PS + R1)")]
        GuidePlusRB,

        [Description("Guide + LB (PS + L1)")]
        GuidePlusLB
    }

    public enum FullscreenTriggerSource
    {
        Connection,
        Hotkey
    }
}
