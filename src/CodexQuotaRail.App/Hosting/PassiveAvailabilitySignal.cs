using CodexQuotaRail.AppServer.RateLimits;

namespace CodexQuotaRail.App.Hosting;

public sealed class PassiveAvailabilitySignal : IRateLimitAvailabilitySignal
{
    public event EventHandler? Paused
    {
        add { }
        remove { }
    }

    public event EventHandler? Resumed
    {
        add { }
        remove { }
    }

    public event EventHandler? NetworkAvailable
    {
        add { }
        remove { }
    }

    public bool IsPaused => false;
}
