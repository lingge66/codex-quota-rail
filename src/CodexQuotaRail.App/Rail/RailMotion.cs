namespace CodexQuotaRail.App.Rail;

public static class RailMotion
{
    private static readonly TimeSpan FocusDuration = TimeSpan.FromMilliseconds(180);

    public static TimeSpan FocusOpacityDuration(bool reduceMotion) =>
        reduceMotion ? TimeSpan.Zero : FocusDuration;

    public static bool ShouldReapplyForDpi(double currentScale, double nextScale) =>
        double.IsFinite(nextScale) &&
        nextScale > 0 &&
        Math.Abs(currentScale - nextScale) > 0.001;
}
