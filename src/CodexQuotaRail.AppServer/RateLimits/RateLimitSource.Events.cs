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

        _callbackDispatcher.Dispatch(
            () => InvokeSnapshotHandlers(handlers, snapshot));
    }

    private void PublishConnection(QuotaConnectionState state)
    {
        var handlers = ConnectionChanged;
        if (handlers is null)
        {
            return;
        }

        _callbackDispatcher.Dispatch(
            () => InvokeConnectionHandlers(handlers, state));
    }

    private void InvokeSnapshotHandlers(
        EventHandler<RawQuotaSnapshot> handlers,
        RawQuotaSnapshot snapshot)
    {
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

    private void InvokeConnectionHandlers(
        EventHandler<QuotaConnectionState> handlers,
        QuotaConnectionState state)
    {
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
        var cleanupFailed = false;
        try
        {
            try
            {
                UnsubscribeAvailability();
            }
            catch
            {
                cleanupFailed = true;
            }

            try
            {
                await StopScheduledRefreshAsync().ConfigureAwait(false);
            }
            catch
            {
                cleanupFailed = true;
            }

            try
            {
                _lifetimeCancellation.Cancel();
            }
            catch
            {
                cleanupFailed = true;
            }

            var worker = _workerTask;
            if (worker is not null)
            {
                try
                {
                    await worker.ConfigureAwait(false);
                }
                catch
                {
                    cleanupFailed = true;
                }
            }

            try
            {
                await DisposeConnectionAsync().ConfigureAwait(false);
            }
            catch
            {
                cleanupFailed = true;
            }
        }
        finally
        {
            CompletePendingWaiters();
            try
            {
                _refreshSignal.Dispose();
            }
            catch
            {
                cleanupFailed = true;
            }

            try
            {
                _lifetimeCancellation.Dispose();
            }
            catch
            {
                cleanupFailed = true;
            }

            if (cleanupFailed)
            {
                completion.TrySetException(
                    new AppServerProtocolException("清理额度来源失败。"));
            }
            else
            {
                completion.TrySetResult();
            }
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
            waiters = [.. _waiters.Values];
            _waiters.Clear();
        }

        var error = new ObjectDisposedException(nameof(RateLimitSource));
        foreach (var waiter in waiters)
        {
            waiter.TrySetException(error);
        }
    }
}
