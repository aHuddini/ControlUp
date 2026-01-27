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
        private FullscreenTriggerMode _fullscreenTriggerMode = FullscreenTriggerMode.NewConnectionOnly;
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

        public bool SkipPopupOnConnection
        {
            get => _skipPopupOnConnection;
            set => SetValue(ref _skipPopupOnConnection, value);
        }

        // Hotkey Settings
        private bool _enableHotkey = true;
        private ControllerHotkey _hotkeyCombo = ControllerHotkey.StartPlusRB;
        private bool _skipPopupOnHotkey = false;
        private bool _requireLongPress = false;
        private int _longPressDelayMs = 500;
        private int _hotkeyCooldownMs = 200;

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

        /// <summary>Require long press instead of instant tap for hotkey activation.</summary>
        public bool RequireLongPress
        {
            get => _requireLongPress;
            set => SetValue(ref _requireLongPress, value);
        }

        /// <summary>Duration in ms to hold hotkey for long press (300-2000). Default: 500ms.</summary>
        public int LongPressDelayMs
        {
            get => _longPressDelayMs;
            set => SetValue(ref _longPressDelayMs, Math.Max(300, Math.Min(2000, value)));
        }

        /// <summary>Cooldown in ms after popup closes before hotkey can trigger again (0-5000). 0 = no cooldown. Default: 500ms.</summary>
        public int HotkeyCooldownMs
        {
            get => _hotkeyCooldownMs;
            set => SetValue(ref _hotkeyCooldownMs, Math.Max(0, Math.Min(5000, value)));
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
        private int _borderOpacity = 70;
        private int _cornerRadius = 0;

        // Effects
        private int _blurWindowPadding = 0;
        private bool _enableVignette = false;
        private string _vignetteColor = "000000";
        private int _vignetteOpacity = 60;
        private int _vignetteSize = 100;

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

        /// <summary>Opacity of border (0-255). Default: 255.</summary>
        public int BorderOpacity
        {
            get => _borderOpacity;
            set => SetValue(ref _borderOpacity, Math.Max(0, Math.Min(255, value)));
        }

        public int CornerRadius
        {
            get => _cornerRadius;
            set => SetValue(ref _cornerRadius, Math.Max(0, Math.Min(32, value)));
        }

        // Effects
        /// <summary>Extra padding around window for blur effect (0-50). Default: 0.</summary>
        public int BlurWindowPadding
        {
            get => _blurWindowPadding;
            set => SetValue(ref _blurWindowPadding, Math.Max(0, Math.Min(50, value)));
        }

        public bool EnableVignette
        {
            get => _enableVignette;
            set => SetValue(ref _enableVignette, value);
        }

        /// <summary>RGB hex without # prefix for vignette color.</summary>
        public string VignetteColor
        {
            get => _vignetteColor;
            set => SetValue(ref _vignetteColor, value ?? "000000");
        }

        /// <summary>Opacity of vignette effect (0-255). Default: 60.</summary>
        public int VignetteOpacity
        {
            get => _vignetteOpacity;
            set => SetValue(ref _vignetteOpacity, Math.Max(0, Math.Min(255, value)));
        }

        /// <summary>Size of vignette effect in pixels (50-200). Default: 100.</summary>
        public int VignetteSize
        {
            get => _vignetteSize;
            set => SetValue(ref _vignetteSize, Math.Max(50, Math.Min(200, value)));
        }

        // Toast Notification Settings
        private bool _enableToastNotifications = true;
        private int _toastPosition = 4; // BottomRight
        private int _toastEdgeMargin = 30;
        private bool _enableToastBlur = true;
        private int _toastBlurOpacity = 180;
        private string _toastBlurTintColor = "1E1E1E";
        private double _toastWidth = 350;
        private double _toastMinHeight = 70;
        private double _toastMaxHeight = 140;
        private int _toastDurationMs = 3000;
        private string _toastBorderColor = "2A2A2A";
        private double _toastBorderThickness = 0;
        private double _toastCornerRadius = 0;

        public bool EnableToastNotifications
        {
            get => _enableToastNotifications;
            set => SetValue(ref _enableToastNotifications, value);
        }

        /// <summary>0=TopRight, 1=TopLeft, 2=BottomRight, 3=BottomLeft, 4=TopCenter, 5=BottomCenter</summary>
        public int ToastPosition
        {
            get => _toastPosition;
            set => SetValue(ref _toastPosition, Math.Max(0, Math.Min(5, value)));
        }

        public int ToastEdgeMargin
        {
            get => _toastEdgeMargin;
            set => SetValue(ref _toastEdgeMargin, Math.Max(0, Math.Min(100, value)));
        }

        public bool EnableToastBlur
        {
            get => _enableToastBlur;
            set => SetValue(ref _enableToastBlur, value);
        }

        public int ToastBlurOpacity
        {
            get => _toastBlurOpacity;
            set => SetValue(ref _toastBlurOpacity, Math.Max(0, Math.Min(255, value)));
        }

        public string ToastBlurTintColor
        {
            get => _toastBlurTintColor;
            set => SetValue(ref _toastBlurTintColor, value ?? "1E1E1E");
        }

        public double ToastWidth
        {
            get => _toastWidth;
            set => SetValue(ref _toastWidth, Math.Max(200, Math.Min(600, value)));
        }

        public double ToastMinHeight
        {
            get => _toastMinHeight;
            set => SetValue(ref _toastMinHeight, Math.Max(50, Math.Min(200, value)));
        }

        public double ToastMaxHeight
        {
            get => _toastMaxHeight;
            set => SetValue(ref _toastMaxHeight, Math.Max(80, Math.Min(300, value)));
        }

        public int ToastDurationMs
        {
            get => _toastDurationMs;
            set => SetValue(ref _toastDurationMs, Math.Max(1000, Math.Min(10000, value)));
        }

        public string ToastBorderColor
        {
            get => _toastBorderColor;
            set => SetValue(ref _toastBorderColor, value ?? "2A2A2A");
        }

        public double ToastBorderThickness
        {
            get => _toastBorderThickness;
            set => SetValue(ref _toastBorderThickness, Math.Max(0, Math.Min(5, value)));
        }

        public double ToastCornerRadius
        {
            get => _toastCornerRadius;
            set => SetValue(ref _toastCornerRadius, Math.Max(0, Math.Min(16, value)));
        }

        // Toast accent/title color (RGB hex without # prefix)
        private string _toastAccentColor = "64B5F6";  // Default info blue

        public string ToastAccentColor
        {
            get => _toastAccentColor;
            set => SetValue(ref _toastAccentColor, value ?? "64B5F6");
        }

        // Toast text color (RGB hex without # prefix)
        private string _toastTextColor = "E0E0E0";

        public string ToastTextColor
        {
            get => _toastTextColor;
            set => SetValue(ref _toastTextColor, value ?? "E0E0E0");
        }

        // Toast accent bar settings
        private bool _enableToastAccentBar = true;
        private double _toastAccentBarThickness = 4;

        public bool EnableToastAccentBar
        {
            get => _enableToastAccentBar;
            set => SetValue(ref _enableToastAccentBar, value);
        }

        public double ToastAccentBarThickness
        {
            get => _toastAccentBarThickness;
            set => SetValue(ref _toastAccentBarThickness, Math.Max(0, Math.Min(10, value)));
        }

        public void BeginEdit() { }
        public void CancelEdit() { }
        public void EndEdit() { }

        public bool IsEqual(ISettings other)
        {
            if (other is ControlUpSettings o)
            {
                return
                    // General Settings
                    FullscreenTriggerMode == o.FullscreenTriggerMode &&
                    EnableLogging == o.EnableLogging &&
                    SkipPopupOnConnection == o.SkipPopupOnConnection &&
                    // Hotkey Settings
                    EnableHotkey == o.EnableHotkey &&
                    HotkeyCombo == o.HotkeyCombo &&
                    SkipPopupOnHotkey == o.SkipPopupOnHotkey &&
                    RequireLongPress == o.RequireLongPress &&
                    LongPressDelayMs == o.LongPressDelayMs &&
                    HotkeyCooldownMs == o.HotkeyCooldownMs &&
                    // Notification Settings
                    NotificationPosition == o.NotificationPosition &&
                    NotificationDurationSeconds == o.NotificationDurationSeconds &&
                    NotificationWidth == o.NotificationWidth &&
                    NotificationHeight == o.NotificationHeight &&
                    NotificationEdgeMargin == o.NotificationEdgeMargin &&
                    // Blur Settings
                    EnableBlur == o.EnableBlur &&
                    BlurOpacity == o.BlurOpacity &&
                    BlurTintColor == o.BlurTintColor &&
                    BlurMode == o.BlurMode &&
                    // Visual Settings
                    BackgroundColor == o.BackgroundColor &&
                    BackgroundOpacity == o.BackgroundOpacity &&
                    BorderColor == o.BorderColor &&
                    BorderThickness == o.BorderThickness &&
                    BorderOpacity == o.BorderOpacity &&
                    CornerRadius == o.CornerRadius &&
                    // Effects
                    BlurWindowPadding == o.BlurWindowPadding &&
                    EnableVignette == o.EnableVignette &&
                    VignetteColor == o.VignetteColor &&
                    VignetteOpacity == o.VignetteOpacity &&
                    VignetteSize == o.VignetteSize &&
                    // Toast Settings
                    EnableToastNotifications == o.EnableToastNotifications &&
                    ToastPosition == o.ToastPosition &&
                    ToastEdgeMargin == o.ToastEdgeMargin &&
                    EnableToastBlur == o.EnableToastBlur &&
                    ToastBlurOpacity == o.ToastBlurOpacity &&
                    ToastBlurTintColor == o.ToastBlurTintColor &&
                    ToastWidth == o.ToastWidth &&
                    ToastMinHeight == o.ToastMinHeight &&
                    ToastMaxHeight == o.ToastMaxHeight &&
                    ToastDurationMs == o.ToastDurationMs &&
                    ToastBorderColor == o.ToastBorderColor &&
                    ToastBorderThickness == o.ToastBorderThickness &&
                    ToastCornerRadius == o.ToastCornerRadius &&
                    ToastAccentColor == o.ToastAccentColor &&
                    ToastTextColor == o.ToastTextColor &&
                    EnableToastAccentBar == o.EnableToastAccentBar &&
                    ToastAccentBarThickness == o.ToastAccentBarThickness;
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
        [Description("Disabled - No automatic switching")]
        Disabled,

        [Description("New Connection Only - Trigger when controller is newly connected")]
        NewConnectionOnly,

        [Description("Any Controller Anytime - Trigger on startup and new connections")]
        AnyControllerAnytime,

        [Description("Startup Only - Only check when Playnite starts")]
        StartupOnly
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
        // Combo hotkeys - Start combos
        [Description("Start + RB (Options + R1)")]
        StartPlusRB,

        [Description("Start + LB (Options + L1)")]
        StartPlusLB,

        [Description("Start + Back (Options + Share)")]
        StartPlusBack,

        // Combo hotkeys - Back/Share combos
        [Description("Back + Start (Share + Options)")]
        BackPlusStart,

        [Description("Back + RB (Share + R1)")]
        BackPlusRB,

        [Description("Back + LB (Share + L1)")]
        BackPlusLB,

        // Combo hotkeys - Guide/PS combos
        [Description("Guide + Start (PS + Options)")]
        GuidePlusStart,

        [Description("Guide + Back (PS + Share)")]
        GuidePlusBack,

        [Description("Guide + RB (PS + R1)")]
        GuidePlusRB,

        [Description("Guide + LB (PS + L1)")]
        GuidePlusLB,

        // Combo hotkeys - Shoulder button combos
        [Description("LB + RB (L1 + R1)")]
        LBPlusRB,

        [Description("LB + RB + Start")]
        LBPlusRBPlusStart,

        [Description("LB + RB + Back")]
        LBPlusRBPlusBack,

        // Single button hotkeys (best used with long press)
        [Description("Guide (PS/Xbox Button)")]
        Guide,

        [Description("Back (Share/View)")]
        Back,

        [Description("Start (Options/Menu)")]
        Start
    }

    public enum FullscreenTriggerSource
    {
        Connection,
        Hotkey
    }

    public enum ToastPositionEnum
    {
        [Description("Top Right")]
        TopRight,

        [Description("Top Left")]
        TopLeft,

        [Description("Bottom Right")]
        BottomRight,

        [Description("Bottom Left")]
        BottomLeft,

        [Description("Top Center")]
        TopCenter,

        [Description("Bottom Center")]
        BottomCenter
    }

    /// <summary>Preset color styles for notification appearance.</summary>
    public enum NotificationStylePreset
    {
        [Description("Custom")]
        Custom,

        [Description("Midnight Blue (Default)")]
        MidnightBlue,

        [Description("Deep Purple")]
        DeepPurple,

        [Description("Forest Green")]
        ForestGreen,

        [Description("Crimson Red")]
        CrimsonRed,

        [Description("Sunset Orange")]
        SunsetOrange,

        [Description("Ocean Teal")]
        OceanTeal,

        [Description("Charcoal Gray")]
        CharcoalGray,

        [Description("Rose Pink")]
        RosePink
    }

    /// <summary>Preset color styles for toast notifications.</summary>
    public enum ToastStylePreset
    {
        [Description("Custom")]
        Custom,

        [Description("Ocean Blue (Default)")]
        OceanBlue,

        [Description("Midnight Purple")]
        MidnightPurple,

        [Description("Forest Green")]
        ForestGreen,

        [Description("Sunset Orange")]
        SunsetOrange,

        [Description("Crimson Red")]
        CrimsonRed,

        [Description("Charcoal Gray")]
        CharcoalGray,

        [Description("Rose Pink")]
        RosePink,

        [Description("Ocean Teal")]
        OceanTeal
    }
}
