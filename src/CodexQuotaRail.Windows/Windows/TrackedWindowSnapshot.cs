namespace CodexQuotaRail.Windows.Windows;

public readonly record struct PixelRect(int Left, int Top, int Width, int Height);

public sealed record TrackedWindowSnapshot(
    nint Handle,
    PixelRect Bounds,
    PixelRect WorkArea,
    double DpiScale,
    bool IsVisible,
    bool IsMinimized,
    bool IsMaximized,
    bool IsForeground);
