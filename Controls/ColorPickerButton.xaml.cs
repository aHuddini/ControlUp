using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ControlUp.Controls
{
    public partial class ColorPickerButton : UserControl
    {
        public static readonly DependencyProperty SelectedColorProperty =
            DependencyProperty.Register(
                nameof(SelectedColor),
                typeof(string),
                typeof(ColorPickerButton),
                new FrameworkPropertyMetadata(
                    "000000",
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnSelectedColorChanged));

        public string SelectedColor
        {
            get => (string)GetValue(SelectedColorProperty);
            set => SetValue(SelectedColorProperty, value);
        }

        public ColorPickerButton()
        {
            InitializeComponent();
            UpdateColorDisplay();
        }

        private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ColorPickerButton picker)
            {
                picker.UpdateColorDisplay();
            }
        }

        private void UpdateColorDisplay()
        {
            var color = HexToColor(SelectedColor ?? "000000");
            ColorPreview.Background = new SolidColorBrush(color);
            ColorHexText.Text = $"#{SelectedColor?.ToUpperInvariant() ?? "000000"}";
        }

        private void PickerButton_Click(object sender, RoutedEventArgs e)
        {
            // Use Windows Forms color dialog
            using (var colorDialog = new System.Windows.Forms.ColorDialog())
            {
                // Set initial color
                var currentColor = HexToColor(SelectedColor ?? "000000");
                colorDialog.Color = System.Drawing.Color.FromArgb(currentColor.R, currentColor.G, currentColor.B);
                colorDialog.FullOpen = true;

                if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var selected = colorDialog.Color;
                    SelectedColor = $"{selected.R:X2}{selected.G:X2}{selected.B:X2}";
                }
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
            return Color.FromRgb(0, 0, 0);
        }
    }
}
