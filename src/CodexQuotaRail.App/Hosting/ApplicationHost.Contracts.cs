using CodexQuotaRail.App.Settings;
using CodexQuotaRail.AppServer.RateLimits;
using CodexQuotaRail.Core.Quotas;
using CodexQuotaRail.Windows.Overlay;

namespace CodexQuotaRail.App.Hosting;

public interface IUiDispatcher
{
    ValueTask InvokeAsync(
        Action action,
        CancellationToken cancellationToken = default);
}

public interface IOverlayPresenter : IDisposable
{
    void ApplySettings(AppSettings settings);

    void Present(
        QuotaDisplayState state,
        OverlayPlacement placement,
        double dpiScale);
}

public interface IApplicationActions
{
    void CheckForUpdates();

    void OpenLogs();

    void ShowTroubleshooting();

    void RequestExit();
}

public interface IAccessibilitySettings
{
    bool ClientAreaAnimationEnabled { get; }
}

public enum SystemPowerTransition
{
    Suspend,
    Resume,
}

public interface ISystemEventSource : IDisposable
{
    event EventHandler<SystemPowerTransition>? PowerChanged;

    event EventHandler? NetworkAvailable;

    event EventHandler? TaskbarRestarted;
}

public interface IDesktopTransitionSignal : IRateLimitAvailabilitySignal, IDisposable
{
    event EventHandler? TaskbarRestarted;
}
