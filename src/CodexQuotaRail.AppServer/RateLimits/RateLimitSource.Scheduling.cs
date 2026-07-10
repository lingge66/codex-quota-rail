namespace CodexQuotaRail.AppServer.RateLimits;

public sealed partial class RateLimitSource
{
    private void ScheduleRefresh(TimeSpan delay)
    {
        if (_dependencies.Availability.IsPaused ||
            _lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        var scheduled = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token);
        CancellationTokenSource? previous;
        lock (_sync)
        {
            previous = _scheduledRefresh;
            _scheduledRefresh = scheduled;
        }

        Cancel(previous);
        _ = WaitAndQueueRefreshAsync(delay, scheduled);
    }

    private async Task WaitAndQueueRefreshAsync(
        TimeSpan delay,
        CancellationTokenSource scheduled)
    {
        try
        {
            await Task.Delay(
                delay,
                _dependencies.TimeProvider,
                scheduled.Token).ConfigureAwait(false);
            QueueRefresh();
        }
        catch (OperationCanceledException) when (scheduled.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_scheduledRefresh, scheduled))
                {
                    _scheduledRefresh = null;
                }
            }

            scheduled.Dispose();
        }
    }

    private void OnPaused(object? sender, EventArgs eventArgs) => CancelScheduledRefresh();

    private void OnResumed(object? sender, EventArgs eventArgs) => TryQueueSignalRefresh();

    private void OnNetworkAvailable(object? sender, EventArgs eventArgs) => TryQueueSignalRefresh();

    private void TryQueueSignalRefresh()
    {
        if (_dependencies.Availability.IsPaused)
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

    private void SubscribeAvailability()
    {
        _dependencies.Availability.Paused += OnPaused;
        _dependencies.Availability.Resumed += OnResumed;
        _dependencies.Availability.NetworkAvailable += OnNetworkAvailable;
    }

    private void UnsubscribeAvailability()
    {
        _dependencies.Availability.Paused -= OnPaused;
        _dependencies.Availability.Resumed -= OnResumed;
        _dependencies.Availability.NetworkAvailable -= OnNetworkAvailable;
    }

    private void CancelScheduledRefresh()
    {
        CancellationTokenSource? scheduled;
        lock (_sync)
        {
            scheduled = _scheduledRefresh;
            _scheduledRefresh = null;
        }

        Cancel(scheduled);
    }

    private static void Cancel(CancellationTokenSource? cancellation)
    {
        cancellation?.Cancel();
    }
}
