namespace CodexQuotaRail.Core.Quotas;

public static class QuotaNormalizer
{
    public static QuotaDisplayState Normalize(RawQuotaSnapshot snapshot)
    {
        var windows = new[] { snapshot.Primary, snapshot.Secondary }
            .Where(window => window is not null)
            .Select(window => NormalizeWindow(window!))
            .ToArray();

        return new QuotaDisplayState(windows, QuotaConnectionState.Live, snapshot.ReceivedAt, null);
    }

    public static QuotaWindowDisplay NormalizeWindow(RawQuotaWindow source)
    {
        if (source.IsUnlimited)
        {
            return new(source.Label, null, Duration(source), Reset(source), QuotaWindowState.Unlimited);
        }

        if (source.UsedPercent is null)
        {
            return new(source.Label, null, Duration(source), Reset(source), QuotaWindowState.Unavailable);
        }

        var available = Math.Clamp(100 - source.UsedPercent.Value, 0, 100);
        var state = available switch
        {
            0 => QuotaWindowState.Exhausted,
            <= 20 => QuotaWindowState.Critical,
            <= 50 => QuotaWindowState.Notice,
            _ => QuotaWindowState.Healthy
        };

        return new(source.Label, available, Duration(source), Reset(source), state);
    }

    private static TimeSpan? Duration(RawQuotaWindow source) =>
        source.WindowDurationMins is long minutes ? TimeSpan.FromMinutes(minutes) : null;

    private static DateTimeOffset? Reset(RawQuotaWindow source) =>
        source.ResetsAtUnixSeconds is long seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
}
