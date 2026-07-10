using CodexQuotaRail.Windows.Windows;

namespace CodexQuotaRail.Windows.Overlay;

public enum OverlayMode
{
    Hidden,
    ExternalRail,
    CompactTitleBar,
}

public sealed record OverlayPlacement(
    PixelRect Bounds,
    OverlayMode Mode,
    double Opacity);

public static class OverlayPlacementCalculator
{
    private const int CompactHeight = 4;
    private const int RailHeight = 22;

    public static OverlayPlacement Calculate(TrackedWindowSnapshot window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!window.IsVisible || window.IsMinimized)
        {
            return new OverlayPlacement(
                new PixelRect(0, 0, 0, 0),
                OverlayMode.Hidden,
                0);
        }

        var opacity = window.IsForeground ? 1.0 : 0.52;
        var hasExternalSpace =
            (long)window.Bounds.Top - window.WorkArea.Top >= RailHeight;
        var mode = !window.IsMaximized && hasExternalSpace
            ? OverlayMode.ExternalRail
            : OverlayMode.CompactTitleBar;
        var requestedHeight = mode == OverlayMode.ExternalRail
            ? RailHeight
            : CompactHeight;
        var requestedTop = mode == OverlayMode.ExternalRail
            ? (long)window.Bounds.Top - RailHeight
            : window.Bounds.Top;

        return new OverlayPlacement(
            ClipToWorkArea(window.Bounds, window.WorkArea, requestedTop, requestedHeight),
            mode,
            opacity);
    }

    private static PixelRect ClipToWorkArea(
        PixelRect window,
        PixelRect workArea,
        long requestedTop,
        int requestedHeight)
    {
        var workWidth = Math.Max(0L, workArea.Width);
        var workHeight = Math.Max(0L, workArea.Height);
        var workLeft = (long)workArea.Left;
        var workTop = (long)workArea.Top;
        var workRight = workLeft + workWidth;
        var workBottom = workTop + workHeight;
        var windowLeft = (long)window.Left;
        var windowRight = windowLeft + Math.Max(0L, window.Width);
        var left = Math.Clamp(windowLeft, workLeft, workRight);
        var right = Math.Clamp(windowRight, left, workRight);
        var height = Math.Min((long)requestedHeight, workHeight);
        var top = Math.Clamp(requestedTop, workTop, workBottom - height);

        return new PixelRect(
            ToInt(left),
            ToInt(top),
            ToInt(right - left),
            ToInt(height));
    }

    private static int ToInt(long value) =>
        (int)Math.Clamp(value, int.MinValue, int.MaxValue);
}
