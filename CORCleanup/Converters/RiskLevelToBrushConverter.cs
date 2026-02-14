using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using CORCleanup.Core.Models;

namespace CORCleanup.Converters;

/// <summary>
/// Converts ActionRiskLevel to a SolidColorBrush for risk level badges.
/// </summary>
public sealed class RiskLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ActionRiskLevel risk)
        {
            return risk switch
            {
                ActionRiskLevel.Safe => new SolidColorBrush(Color.FromRgb(76, 175, 80)),     // Green
                ActionRiskLevel.Low => new SolidColorBrush(Color.FromRgb(33, 150, 243)),     // Blue
                ActionRiskLevel.Medium => new SolidColorBrush(Color.FromRgb(255, 152, 0)),   // Orange
                ActionRiskLevel.High => new SolidColorBrush(Color.FromRgb(244, 67, 54)),     // Red
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
