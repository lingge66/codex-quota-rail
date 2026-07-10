using CodexQuotaRail.Core.Quotas;

namespace CodexQuotaRail.AppServer.RateLimits;

public interface IRateLimitSource : IAsyncDisposable
{
    event EventHandler<RawQuotaSnapshot>? SnapshotChanged;

    event EventHandler<QuotaConnectionState>? ConnectionChanged;

    Task StartAsync(CancellationToken cancellationToken);

    Task RefreshAsync(CancellationToken cancellationToken);
}
