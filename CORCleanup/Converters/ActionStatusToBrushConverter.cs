using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using CORCleanup.Core.Models;

namespace CORCleanup.Converters;

/// <summary>
/// Converts ActionStatus to a SolidColorBrush for status indicators.
/// </summary>
public sealed class ActionStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ActionStatus status)
        {
            return status switch
            {
                ActionStatus.Pending => new SolidColorBrush(Color.FromRgb(128, 128, 128)),   // Grey
                ActionStatus.Running => new SolidColorBrush(Color.FromRgb(33, 150, 243)),    // Blue
                ActionStatus.Completed => new SolidColorBrush(Color.FromRgb(76, 175, 80)),   // Green
                ActionStatus.Failed => new SolidColorBrush(Color.FromRgb(244, 67, 54)),      // Red
                ActionStatus.Skipped => new SolidColorBrush(Color.FromRgb(255, 193, 7)),     // Amber
                _ => new SolidColorBrush(Color.FromRgb(128, 128, 128))
            };
        }
        return new SolidColorBrush(Color.FromRgb(128, 128, 128));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
