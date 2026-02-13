using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CORCleanup.Converters;

/// <summary>
/// Converts a string value to Visibility.
/// Non-null/non-empty string = Visible; null/empty = Collapsed.
/// </summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.IsNullOrEmpty(value as string)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
