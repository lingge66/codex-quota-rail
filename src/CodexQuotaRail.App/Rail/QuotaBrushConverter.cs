using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CodexQuotaRail.Core.Quotas;

namespace CodexQuotaRail.App.Rail;

public sealed class QuotaBrushConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (value is not RgbColor color)
        {
            return DependencyProperty.UnsetValue;
        }

        var brush = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}
