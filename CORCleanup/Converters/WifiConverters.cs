using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace CORCleanup.Converters;

/// <summary>
/// Converts a percentage (0-100) to a pixel height/width.
/// ConverterParameter specifies the max dimension (default 200).
/// </summary>
public class PercentToHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int percent)
        {
            double max = parameter is string s && double.TryParse(s, CultureInfo.InvariantCulture, out var h) ? h : 200;
            return Math.Max(2, percent / 100.0 * max);
        }
        return 2.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts a signal percentage to a colour brush (green/amber/red).
/// </summary>
public class SignalToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Good = new(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly SolidColorBrush Fair = new(Color.FromRgb(0xD4, 0xA8, 0x43));
    private static readonly SolidColorBrush Poor = new(Color.FromRgb(0xE0, 0x66, 0x66));

    static SignalToBrushConverter()
    {
        Good.Freeze();
        Fair.Freeze();
        Poor.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int percent)
        {
            return percent switch
            {
                >= 60 => Good,
                >= 30 => Fair,
                _ => Poor
            };
        }
        return Good;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Returns Visible when an integer is greater than zero.
/// </summary>
public class IntGreaterThanZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
            return count > 0 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
