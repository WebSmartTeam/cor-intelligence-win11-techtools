using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace CORCleanup.Converters;

/// <summary>
/// Converts a boolean to ControlAppearance.
/// true = Primary (accent colour); false = Secondary (default).
/// </summary>
public sealed class BoolToAppearanceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true
            ? ControlAppearance.Primary
            : ControlAppearance.Secondary;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
