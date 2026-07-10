using System.Text.Json;
using CodexQuotaRail.AppServer.Discovery;
using CodexQuotaRail.AppServer.Protocol;
using CodexQuotaRail.Core.Quotas;

namespace CodexQuotaRail.AppServer.RateLimits;

public sealed partial class RateLimitSource
{
    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        if (_dependencies.Availability.IsPaused)
        {
            return;
        }

        try
        {
            var connection = await EnsureConnectionAsync(cancellationToken).ConfigureAwait(false);
            if (!_hasAuthenticatedAccount)
            {
                var account = await connection.RequestAsync(
                    "account/read",
                    new { },
                    cancellationToken).ConfigureAwait(false);
                if (!ReadAccountAvailability(account))
                {
                    _backoff.Reset();
                    PublishConnection(QuotaConnectionState.AuthenticationRequired);
                    await ScheduleRefreshAsync(RefreshInterval).ConfigureAwait(false);
                    return;
                }

                _hasAuthenticatedAccount = true;
            }

            var result = await connection.RequestAsync(
                "account/rateLimits/read",
                new { },
                cancellationToken).ConfigureAwait(false);
            var snapshot = RateLimitSnapshotMapper.Map(
                result,
                _dependencies.TimeProvider.GetUtcNow());
            _hasSnapshot = true;
            _backoff.Reset();
            PublishSnapshot(snapshot);
            PublishConnection(QuotaConnectionState.Live);
            await ScheduleRefreshAsync(RefreshInterval).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (UnsupportedCodexException)
        {
            await DisposeConnectionAsync().ConfigureAwait(false);
            PublishConnection(QuotaConnectionState.Unsupported);
            await ScheduleRefreshAsync(RefreshInterval).ConfigureAwait(false);
        }
        catch
        {
            await DisposeConnectionAsync().ConfigureAwait(false);
            PublishConnection(
                _hasSnapshot ? QuotaConnectionState.Stale : QuotaConnectionState.Unavailable);
            await ScheduleRefreshAsync(_backoff.NextDelay()).ConfigureAwait(false);
        }
    }

    private async Task<IRateLimitConnection> EnsureConnectionAsync(
        CancellationToken cancellationToken)
    {
        if (_connection is not null)
        {
            return _connection;
        }

        PublishConnection(QuotaConnectionState.Connecting);
        var resolution = _dependencies.ExecutableResolver.Resolve();
        if (resolution is CodexResolution.Missing)
        {
            throw new AppServerProtocolException("未找到 Codex。");
        }

        if (resolution is CodexResolution.Unsupported)
        {
            throw new UnsupportedCodexException();
        }

        var found = (CodexResolution.Found)resolution;
        var launchSpec = CodexExecutableResolver.CreateAppServerLaunchSpec(found);
        var connection = _dependencies.ConnectionFactory.Create(launchSpec);
        connection.NotificationReceived += OnNotificationReceived;
        try
        {
            await connection.StartAsync(cancellationToken).ConfigureAwait(false);
            await connection.InitializeAsync(
                _dependencies.ClientVersion,
                cancellationToken).ConfigureAwait(false);
            _connection = connection;
            return connection;
        }
        catch
        {
            connection.NotificationReceived -= OnNotificationReceived;
            await SafeDisposeAsync(connection).ConfigureAwait(false);
            throw;
        }
    }

    private static bool ReadAccountAvailability(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("account", out var account))
        {
            throw new AppServerProtocolException("账户响应缺少必要字段。");
        }

        return account.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
    }

    private void OnNotificationReceived(object? sender, JsonRpcNotification notification)
    {
        if (notification.Method != "account/rateLimits/updated")
        {
            return;
        }

        try
        {
            QueueRefresh();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private sealed class UnsupportedCodexException : Exception
    {
    }
}
