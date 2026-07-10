using System.Globalization;
using System.Windows.Media;
using CodexQuotaRail.App.Rail;
using CodexQuotaRail.Core.Quotas;

namespace CodexQuotaRail.App.Tests;

public sealed class QuotaBrushConverterTests
{
    [Fact]
    public void ConvertCreatesFrozenBrushWithExactRgbColor()
    {
        var converter = new QuotaBrushConverter();

        var converted = converter.Convert(
            new RgbColor(145, 239, 107),
            typeof(Brush),
            string.Empty,
            CultureInfo.InvariantCulture);

        var brush = Assert.IsType<SolidColorBrush>(converted);
        Assert.Equal(Color.FromRgb(145, 239, 107), brush.Color);
        Assert.True(brush.IsFrozen);
    }

    [Fact]
    public void ConvertRejectsValuesThatAreNotRgbColors()
    {
        var converter = new QuotaBrushConverter();

        var converted = converter.Convert(
            "#91EF6B",
            typeof(Brush),
            string.Empty,
            CultureInfo.InvariantCulture);

        Assert.Equal(System.Windows.DependencyProperty.UnsetValue, converted);
    }
}
