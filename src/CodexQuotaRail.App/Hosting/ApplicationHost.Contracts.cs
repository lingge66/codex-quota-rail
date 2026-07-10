using CodexQuotaRail.App.Settings;
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
