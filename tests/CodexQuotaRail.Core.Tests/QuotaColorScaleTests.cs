using CodexQuotaRail.Core.Quotas;

namespace CodexQuotaRail.Core.Tests;

public sealed class QuotaColorScaleTests
{
    [Theory]
    [InlineData(100, 145, 239, 107)]
    [InlineData(51, 201, 239, 99)]
    [InlineData(21, 255, 196, 91)]
    [InlineData(1, 255, 97, 93)]
    public void ReturnsExactAnchorColors(int available, byte red, byte green, byte blue)
    {
        var result = QuotaColorScale.ForAvailable(available);

        Assert.Equal(new RgbColor(red, green, blue), result);
    }

    [Theory]
    [InlineData(11, 255, 147, 92)]
    [InlineData(36, 228, 218, 95)]
    [InlineData(76, 172, 239, 103)]
    public void InterpolatesEachChannelBetweenAnchors(
        int available,
        byte red,
        byte green,
        byte blue)
    {
        var result = QuotaColorScale.ForAvailable(available);

        Assert.Equal(new RgbColor(red, green, blue), result);
    }

    [Theory]
    [InlineData(-1, 255, 97, 93)]
    [InlineData(101, 145, 239, 107)]
    public void ClampsValuesOutsideDisplayRange(int available, byte red, byte green, byte blue)
    {
        var result = QuotaColorScale.ForAvailable(available);

        Assert.Equal(new RgbColor(red, green, blue), result);
    }
}
