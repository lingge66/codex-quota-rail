using CodexQuotaRail.AppServer.Protocol;
using CodexQuotaRail.Core.Quotas;

namespace CodexQuotaRail.AppServer.RateLimits;

public sealed partial class RateLimitSource
{
    private void PublishSnapshot(RawQuotaSnapshot snapshot)
    {
        var handlers = SnapshotChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (var subscriber in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<RawQuotaSnapshot>)subscriber)(this, snapshot);
            }
            catch
            {
            }
        }
    }

    private void PublishConnection(QuotaConnectionState state)
    {
        var handlers = ConnectionChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (var subscriber in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<QuotaConnectionState>)subscriber)(this, state);
            }
            catch
            {
            }
        }
    }

    private async Task CompleteDisposeAsync(TaskCompletionSource completion)
    {
        try
        {
            UnsubscribeAvailability();
            CancelScheduledRefresh();
            _lifetimeCancellation.Cancel();
            var worker = _workerTask;
            if (worker is not null)
            {
                await worker.ConfigureAwait(false);
            }

            await DisposeConnectionAsync().ConfigureAwait(false);
            _refreshSignal.Dispose();
            _lifetimeCancellation.Dispose();
            completion.TrySetResult();
        }
        catch
        {
            completion.TrySetException(
                new AppServerProtocolException("清理额度来源失败。"));
        }
    }

    private async Task DisposeConnectionAsync()
    {
        var connection = _connection;
        _connection = null;
        _hasAuthenticatedAccount = false;
        if (connection is null)
        {
            return;
        }

        connection.NotificationReceived -= OnNotificationReceived;
        await SafeDisposeAsync(connection).ConfigureAwait(false);
    }

    private static async Task SafeDisposeAsync(IRateLimitConnection connection)
    {
        try
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private void CompletePendingWaiters()
    {
        TaskCompletionSource[] waiters;
        lock (_sync)
        {
            waiters = [.. _waiters];
            _waiters.Clear();
        }

        var error = new ObjectDisposedException(nameof(RateLimitSource));
        foreach (var waiter in waiters)
        {
            waiter.TrySetException(error);
        }
    }
}
