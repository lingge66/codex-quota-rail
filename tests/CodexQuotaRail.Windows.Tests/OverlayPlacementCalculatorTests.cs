using CodexQuotaRail.Windows.Overlay;
using CodexQuotaRail.Windows.Windows;

namespace CodexQuotaRail.Windows.Tests;

public sealed class OverlayPlacementCalculatorTests
{
    [Fact]
    public void CalculatePlacesFocusedNormalWindowAboveItsTopEdge()
    {
        var window = Snapshot(bounds: new PixelRect(100, 100, 800, 600));

        var placement = OverlayPlacementCalculator.Calculate(window);

        Assert.Equal(OverlayMode.ExternalRail, placement.Mode);
        Assert.Equal(new PixelRect(100, 78, 800, 22), placement.Bounds);
        Assert.Equal(1.0, placement.Opacity);
    }

    [Fact]
    public void CalculateDimsVisibleWindowWhenItIsNotForeground()
    {
        var window = Snapshot(
            bounds: new PixelRect(100, 100, 800, 600),
            isForeground: false);

        var placement = OverlayPlacementCalculator.Calculate(window);

        Assert.Equal(OverlayMode.ExternalRail, placement.Mode);
        Assert.Equal(0.52, placement.Opacity);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void CalculateHidesInvisibleOrMinimizedWindow(
        bool isVisible,
        bool isMinimized)
    {
        var window = Snapshot(
            bounds: new PixelRect(100, 100, 800, 600),
            isVisible: isVisible,
            isMinimized: isMinimized);

        var placement = OverlayPlacementCalculator.Calculate(window);

        Assert.Equal(OverlayMode.Hidden, placement.Mode);
        Assert.Equal(new PixelRect(0, 0, 0, 0), placement.Bounds);
        Assert.Equal(0, placement.Opacity);
    }

    [Fact]
    public void CalculateUsesCompactTitleBarForMaximizedWindow()
    {
        var window = Snapshot(
            bounds: new PixelRect(0, 0, 1920, 1040),
            isMaximized: true);

        var placement = OverlayPlacementCalculator.Calculate(window);

        Assert.Equal(OverlayMode.CompactTitleBar, placement.Mode);
        Assert.Equal(new PixelRect(0, 0, 1920, 4), placement.Bounds);
    }

    [Fact]
    public void CalculateUsesCompactTitleBarWhenExternalSpaceIsTooSmall()
    {
        var window = Snapshot(bounds: new PixelRect(100, 20, 800, 600));

        var placement = OverlayPlacementCalculator.Calculate(window);

        Assert.Equal(OverlayMode.CompactTitleBar, placement.Mode);
        Assert.Equal(new PixelRect(100, 20, 800, 4), placement.Bounds);
    }

    [Fact]
    public void CalculateClipsEveryVisiblePlacementToMonitorWorkArea()
    {
        var window = Snapshot(bounds: new PixelRect(-100, -8, 2200, 1200));

        var placement = OverlayPlacementCalculator.Calculate(window);

        Assert.Equal(OverlayMode.CompactTitleBar, placement.Mode);
        Assert.Equal(new PixelRect(0, 0, 1920, 4), placement.Bounds);
    }

    private static TrackedWindowSnapshot Snapshot(
        PixelRect bounds,
        bool isVisible = true,
        bool isMinimized = false,
        bool isMaximized = false,
        bool isForeground = true) =>
        new(
            Handle: (nint)42,
            Bounds: bounds,
            WorkArea: new PixelRect(0, 0, 1920, 1040),
            DpiScale: 1.0,
            IsVisible: isVisible,
            IsMinimized: isMinimized,
            IsMaximized: isMaximized,
            IsForeground: isForeground);
}
