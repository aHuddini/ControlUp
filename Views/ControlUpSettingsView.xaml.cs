using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ControlUp
{
    /// <summary>
    /// Interaction logic for ControlUpSettingsView.xaml
    /// </summary>
    public partial class ControlUpSettingsView : UserControl
    {
        private ControlUpSettingsViewModel _viewModel;

        public ControlUpSettingsView(ControlUpSettingsViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
        }
    }

    /// <summary>
    /// Converter to display enum descriptions in ComboBox
    /// </summary>
    public class EnumDescriptionConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value == null) return null;

            var field = value.GetType().GetField(value.ToString());
            if (field != null)
            {
                var attribute = field.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();
                if (attribute != null)
                {
                    return attribute.Description;
                }
            }

            // Fallback to enum name with spaces
            return System.Text.RegularExpressions.Regex.Replace(value.ToString(), "([a-z])([A-Z])", "$1 $2");
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}