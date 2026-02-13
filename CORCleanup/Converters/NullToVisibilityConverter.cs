using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CORCleanup.Converters;

/// <summary>
/// Converts any object to Visibility based on null check.
/// Non-null = Visible; null = Collapsed.
/// </summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
