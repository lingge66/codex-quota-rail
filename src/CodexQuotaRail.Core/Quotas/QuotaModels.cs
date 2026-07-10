namespace CodexQuotaRail.Core.Quotas;

public enum QuotaConnectionState
{
    Connecting,
    Live,
    Stale,
    AuthenticationRequired,
    Unsupported,
    Unavailable
}

public enum QuotaWindowState
{
    Healthy,
    Notice,
    Critical,
    Exhausted,
    Unlimited,
    Unavailable
}

public sealed record RawQuotaWindow(
    string Label,
    int? UsedPercent,
    long? WindowDurationMins,
    long? ResetsAtUnixSeconds,
    bool IsUnlimited);

public sealed record RawQuotaSnapshot(
    RawQuotaWindow? Primary,
    RawQuotaWindow? Secondary,
    DateTimeOffset ReceivedAt);

public sealed record QuotaWindowDisplay(
    string Label,
    int? AvailablePercent,
    TimeSpan? WindowDuration,
    DateTimeOffset? ResetsAt,
    QuotaWindowState State);

public sealed record QuotaDisplayState(
    IReadOnlyList<QuotaWindowDisplay> Windows,
    QuotaConnectionState Connection,
    DateTimeOffset? UpdatedAt,
    string? Message)
{
    public static QuotaDisplayState Waiting(string message) =>
        new(Array.Empty<QuotaWindowDisplay>(), QuotaConnectionState.Connecting, null, message);
}
