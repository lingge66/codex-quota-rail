namespace CodexQuotaRail.AppServer.RateLimits;

public interface IRateLimitAvailabilitySignal
{
    event EventHandler? Paused;

    event EventHandler? Resumed;

    event EventHandler? NetworkAvailable;

    bool IsPaused { get; }
}
