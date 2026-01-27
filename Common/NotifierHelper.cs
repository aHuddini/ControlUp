using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Playnite.SDK;

namespace ControlUp.Common
{
    /// <summary>
    /// Helper class for showing non-blocking toast notifications.
    /// Adapted from UniPlaySong's DialogHelper.
    /// </summary>
    public static class NotifierHelper
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        // ============================================================================
        // Color Constants
        // ============================================================================

        /// Toast background (#1E1E1E)
        public static readonly Color ToastBackgroundColor = Color.FromRgb(30, 30, 30);

        /// Success border - Material Design green (#4CAF50)
        public static readonly Color ToastSuccessBorderColor = Color.FromRgb(76, 175, 80);

        /// Success accent (#81C784)
        public static readonly Color ToastSuccessAccentColor = Color.FromRgb(129, 199, 132);

        /// Info border - Material Design blue (#2196F3)
        public static readonly Color ToastInfoBorderColor = Color.FromRgb(33, 150, 243);

        /// Info accent (#64B5F6)
        public static readonly Color ToastInfoAccentColor = Color.FromRgb(100, 181, 246);

        /// Error border - Material Design red (#F44336)
        public static readonly Color ToastErrorBorderColor = Color.FromRgb(244, 67, 54);

        /// Error accent (#FF8A80)
        public static readonly Color ToastErrorAccentColor = Color.FromRgb(255, 138, 128);

        /// Toast text (#E0E0E0)
        public static readonly Color ToastTextColor = Color.FromRgb(224, 224, 224);

        // ============================================================================
        // Configurable Toast Settings (synced from ControlUpSettings)
        // ============================================================================

        public enum ToastPosition { TopRight, TopLeft, BottomRight, BottomLeft, TopCenter, BottomCenter }

        /// Whether toast notifications are enabled
        public static bool EnableToasts = true;

        /// Where toasts appear on screen (default: BottomRight)
        public static ToastPosition CurrentToastPosition = ToastPosition.BottomRight;

        /// Margin from screen edge in pixels (default: 30)
        public static int ToastEdgeMargin = 30;

        /// Enable acrylic blur effect (default: true)
        public static bool EnableToastBlur = true;

        /// Blur opacity 0-255 (default: 180)
        public static byte ToastBlurOpacity = 180;

        /// Blur tint color as 0xRRGGBB (default: #1E1E1E)
        public static uint ToastBlurTintColor = 0x1E1E1E;

        /// Toast width in pixels (default: 350)
        public static double ToastWidth = 350;

        /// Toast min height in pixels (default: 70)
        public static double ToastMinHeight = 70;

        /// Toast max height in pixels (default: 140)
        public static double ToastMaxHeight = 140;

        /// Display duration in ms (default: 3000)
        public static int ToastDurationMs = 3000;

        /// Border color as 0xRRGGBB (default: #2A2A2A)
        public static uint ToastBorderColorValue = 0x2A2A2A;

        /// Border thickness (default: 1)
        public static double ToastBorderThickness = 1;

        /// Corner radius (default: 0 to avoid blur artifacts)
        public static double ToastCornerRadius = 0;

        /// Accent/title color as 0xRRGGBB (default: #64B5F6 - info blue)
        public static uint ToastAccentColorValue = 0x64B5F6;

        /// Text color as 0xRRGGBB (default: #E0E0E0)
        public static uint ToastTextColorValue = 0xE0E0E0;

        /// Whether to show the accent bar on the left side
        public static bool EnableAccentBar = true;

        /// Accent bar thickness in pixels (default: 4)
        public static double AccentBarThickness = 4;


        // ============================================================================
        // Windows Acrylic Blur Effect API
        // ============================================================================

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
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public AccentState AccentState;
            public uint AccentFlags;
            public uint GradientColor;
            public int AnimationId;
        }

        // ============================================================================
        // Helper Methods
        // ============================================================================

        private static uint ParseHexColor(string hexColor, uint defaultValue)
        {
            if (string.IsNullOrEmpty(hexColor)) return defaultValue;
            try
            {
                var hex = hexColor.TrimStart('#');
                return hex.Length == 6 ? Convert.ToUInt32(hex, 16) : defaultValue;
            }
            catch { return defaultValue; }
        }

        private static (byte R, byte G, byte B) HexToRgb(uint hexColor) =>
            ((byte)((hexColor >> 16) & 0xFF), (byte)((hexColor >> 8) & 0xFF), (byte)(hexColor & 0xFF));

        /// <summary>Syncs toast settings from ControlUpSettings.</summary>
        public static void SyncSettings(ControlUpSettings settings)
        {
            if (settings == null) return;

            EnableToasts = settings.EnableToastNotifications;
            EnableToastBlur = settings.EnableToastBlur;
            ToastBlurOpacity = (byte)settings.ToastBlurOpacity;
            ToastWidth = settings.ToastWidth;
            ToastMinHeight = settings.ToastMinHeight;
            ToastMaxHeight = settings.ToastMaxHeight;
            ToastDurationMs = settings.ToastDurationMs;
            ToastEdgeMargin = settings.ToastEdgeMargin;
            ToastBorderThickness = settings.ToastBorderThickness;
            ToastCornerRadius = settings.ToastCornerRadius;
            CurrentToastPosition = (ToastPosition)settings.ToastPosition;

            ToastBlurTintColor = ParseHexColor(settings.ToastBlurTintColor, ToastBlurTintColor);
            ToastBorderColorValue = ParseHexColor(settings.ToastBorderColor, ToastBorderColorValue);
            ToastAccentColorValue = ParseHexColor(settings.ToastAccentColor, ToastAccentColorValue);
            ToastTextColorValue = ParseHexColor(settings.ToastTextColor, ToastTextColorValue);
            EnableAccentBar = settings.EnableToastAccentBar;
            AccentBarThickness = settings.ToastAccentBarThickness;
        }

        private static void EnableWindowBlur(Window window)
        {
            try
            {
                var windowHelper = new WindowInteropHelper(window);
                var hwnd = windowHelper.EnsureHandle();

                var (r, g, b) = HexToRgb(ToastBlurTintColor);
                uint gradientColor = ((uint)ToastBlurOpacity << 24) | ((uint)b << 16) | ((uint)g << 8) | r;

                var accent = new AccentPolicy
                {
                    AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
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
                    if (result == 0)
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
            catch (Exception ex)
            {
                Logger.Debug($"EnableWindowBlur failed: {ex.Message}");
            }
        }

        private static void PositionToastWindow(Window toastWindow)
        {
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            var toastWidth = toastWindow.ActualWidth;
            var toastHeight = toastWindow.ActualHeight;
            var margin = ToastEdgeMargin;

            switch (CurrentToastPosition)
            {
                case ToastPosition.TopRight:
                    toastWindow.Left = screenWidth - toastWidth - margin;
                    toastWindow.Top = margin;
                    break;
                case ToastPosition.TopLeft:
                    toastWindow.Left = margin;
                    toastWindow.Top = margin;
                    break;
                case ToastPosition.BottomRight:
                    toastWindow.Left = screenWidth - toastWidth - margin;
                    toastWindow.Top = screenHeight - toastHeight - margin;
                    break;
                case ToastPosition.BottomLeft:
                    toastWindow.Left = margin;
                    toastWindow.Top = screenHeight - toastHeight - margin;
                    break;
                case ToastPosition.TopCenter:
                    toastWindow.Left = (screenWidth - toastWidth) / 2;
                    toastWindow.Top = margin;
                    break;
                case ToastPosition.BottomCenter:
                    toastWindow.Left = (screenWidth - toastWidth) / 2;
                    toastWindow.Top = screenHeight - toastHeight - margin;
                    break;
                default:
                    toastWindow.Left = screenWidth - toastWidth - margin;
                    toastWindow.Top = screenHeight - toastHeight - margin;
                    break;
            }
        }

        // ============================================================================
        // Public Toast Methods
        // ============================================================================

        /// <summary>Shows an auto-closing toast notification (non-blocking, works in fullscreen).</summary>
        public static void ShowToast(string message, string title, ToastType type = ToastType.Info, int? durationMs = null)
        {
            if (!EnableToasts) return;

            try
            {
                var app = Application.Current;
                if (app == null) return;

                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        CreateAndShowToast(message, title, type, durationMs);
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug($"Error showing toast: {ex.Message}");
                    }
                }));
            }
            catch (Exception ex)
            {
                Logger.Debug($"Error dispatching toast: {ex.Message}");
            }
        }

        /// <summary>Shows an info toast.</summary>
        public static void ShowInfo(string message, string title = "Info", int? durationMs = null)
            => ShowToast(message, title, ToastType.Info, durationMs);

        /// <summary>Shows a success toast.</summary>
        public static void ShowSuccess(string message, string title = "Success", int? durationMs = null)
            => ShowToast(message, title, ToastType.Success, durationMs);

        /// <summary>Shows an error toast (25% longer duration).</summary>
        public static void ShowError(string message, string title = "Error", int? durationMs = null)
        {
            var actualDuration = durationMs ?? (int)(ToastDurationMs * 1.25);
            ShowToast(message, title, ToastType.Error, actualDuration);
        }

        public enum ToastType { Info, Success, Error }

        private static void CreateAndShowToast(string message, string title, ToastType type, int? durationMs)
        {
            Window toastWindow = null;
            DispatcherTimer closeTimer = null;

            // Get colors based on type - use custom colors from settings for Info type
            Color accentColor, borderColor;
            var (accentR, accentG, accentB) = HexToRgb(ToastAccentColorValue);
            var (textR, textG, textB) = HexToRgb(ToastTextColorValue);
            var customAccentColor = Color.FromRgb(accentR, accentG, accentB);
            var customTextColor = Color.FromRgb(textR, textG, textB);

            switch (type)
            {
                case ToastType.Success:
                    accentColor = ToastSuccessAccentColor;
                    borderColor = ToastSuccessBorderColor;
                    break;
                case ToastType.Error:
                    accentColor = ToastErrorAccentColor;
                    borderColor = ToastErrorBorderColor;
                    break;
                default:
                    // Use custom colors from settings for Info type
                    accentColor = customAccentColor;
                    var (borderR2, borderG2, borderB2) = HexToRgb(ToastBorderColorValue);
                    borderColor = Color.FromRgb(borderR2, borderG2, borderB2);
                    break;
            }

            // Create content grid with optional accent bar
            var contentGrid = new Grid();

            if (EnableAccentBar && AccentBarThickness > 0)
            {
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(AccentBarThickness) });
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Accent bar
                var accentBar = new System.Windows.Shapes.Rectangle
                {
                    Fill = new SolidColorBrush(borderColor),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Margin = new Thickness(0.5, 0.5, 0, 0.5)
                };
                Grid.SetColumn(accentBar, 0);
                contentGrid.Children.Add(accentBar);
            }
            else
            {
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            // Text content grid
            var textGrid = new Grid();
            textGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            textGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(textGrid, EnableAccentBar && AccentBarThickness > 0 ? 1 : 0);
            contentGrid.Children.Add(textGrid);

            // Title
            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(accentColor),
                Margin = new Thickness(12, 12, 16, 4),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetRow(titleBlock, 0);
            textGrid.Children.Add(titleBlock);

            // Message
            var messageBlock = new TextBlock
            {
                Text = message,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(customTextColor),
                Margin = new Thickness(12, 2, 16, 12),
                HorizontalAlignment = HorizontalAlignment.Left,
                TextAlignment = TextAlignment.Left
            };
            Grid.SetRow(messageBlock, 1);
            textGrid.Children.Add(messageBlock);

            // Outer border
            var (borderR, borderG, borderB) = HexToRgb(ToastBorderColorValue);
            var outerBorderColor = Color.FromRgb(borderR, borderG, borderB);

            var outerBorder = new Border
            {
                BorderBrush = new SolidColorBrush(outerBorderColor),
                BorderThickness = new Thickness(ToastBorderThickness),
                CornerRadius = new CornerRadius(ToastCornerRadius),
                Background = new SolidColorBrush(Color.FromArgb(1, 45, 45, 45)),
                Padding = new Thickness(0),
                Child = contentGrid
            };

            var clipWrapper = new Grid { ClipToBounds = true };
            clipWrapper.Children.Add(outerBorder);

            // Create window
            toastWindow = new Window
            {
                Title = title,
                Width = ToastWidth,
                SizeToContent = SizeToContent.Height,
                MinHeight = ToastMinHeight,
                MaxHeight = ToastMaxHeight,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                Content = clipWrapper,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false
            };

            // Apply rounded clip
            clipWrapper.Loaded += (s, e) =>
            {
                try
                {
                    var wrapper = s as Grid;
                    if (wrapper != null && wrapper.ActualWidth > 0 && wrapper.ActualHeight > 0)
                    {
                        var clipGeometry = new RectangleGeometry(
                            new Rect(0, 0, wrapper.ActualWidth, wrapper.ActualHeight),
                            ToastCornerRadius, ToastCornerRadius);
                        wrapper.Clip = clipGeometry;
                    }
                }
                catch { }
            };

            // Apply blur effect
            toastWindow.SourceInitialized += (s, e) =>
            {
                try
                {
                    if (EnableToastBlur)
                    {
                        EnableWindowBlur(toastWindow);
                    }
                }
                catch { }
            };

            // Position after load
            toastWindow.Loaded += (s, e) =>
            {
                try
                {
                    PositionToastWindow(toastWindow);
                }
                catch
                {
                    toastWindow.Left = SystemParameters.PrimaryScreenWidth - toastWindow.ActualWidth - ToastEdgeMargin;
                    toastWindow.Top = SystemParameters.PrimaryScreenHeight - toastWindow.ActualHeight - ToastEdgeMargin;
                }
            };

            // Auto-close timer
            var actualDuration = durationMs ?? ToastDurationMs;
            closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(actualDuration) };
            closeTimer.Tick += (s, e) =>
            {
                closeTimer.Stop();
                try { toastWindow?.Close(); } catch { }
            };

            // Click to dismiss
            toastWindow.MouseDown += (s, e) =>
            {
                closeTimer?.Stop();
                try { toastWindow?.Close(); } catch { }
            };

            closeTimer.Start();
            toastWindow.Show();
        }
    }
}
