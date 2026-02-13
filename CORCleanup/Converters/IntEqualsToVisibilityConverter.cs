using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CORCleanup.Converters;

/// <summary>
/// Converts an int to Visibility by comparing against a parameter value.
/// If the int equals the parameter, returns Visible; otherwise Collapsed.
/// Usage: Converter={StaticResource IntEqualsToVisConverter}, ConverterParameter=5
/// </summary>
public sealed class IntEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue && parameter is string paramStr && int.TryParse(paramStr, out int target))
            return intValue == target ? Visibility.Visible : Visibility.Collapsed;

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
