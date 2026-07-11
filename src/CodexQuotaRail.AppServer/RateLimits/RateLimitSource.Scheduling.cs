namespace CodexQuotaRail.AppServer.RateLimits;

public sealed partial class RateLimitSource
{
    private async Task ScheduleRefreshAsync(TimeSpan delay)
    {
        await StopScheduledRefreshAsync().ConfigureAwait(false);
        lock (_sync)
        {
            if (_dependencies.Availability.IsPaused ||
                _lifetimeCancellation.IsCancellationRequested ||
                _disposeTask is not null)
            {
                return;
            }

            _scheduledRefresh = new ScheduledRefresh(
                delay,
                _dependencies.TimeProvider,
                TryQueueSignalRefresh);
        }
    }

    private void OnPaused(object? sender, EventArgs eventArgs) =>
        _ = StopScheduledRefreshAsync();

    private void OnResumed(object? sender, EventArgs eventArgs) => TryQueueSignalRefresh();

    private void OnNetworkAvailable(object? sender, EventArgs eventArgs)
    {
        _backoff.Reset();
        TryQueueSignalRefresh();
    }

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

    private async Task StopScheduledRefreshAsync()
    {
        ScheduledRefresh? scheduled;
        lock (_sync)
        {
            scheduled = _scheduledRefresh;
            _scheduledRefresh = null;
        }

        if (scheduled is not null)
        {
            await scheduled.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class ScheduledRefresh : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private readonly object _sync = new();
        private Task? _disposeTask;
        private readonly Task _runTask;

        public ScheduledRefresh(
            TimeSpan delay,
            TimeProvider timeProvider,
            Action callback)
        {
            _runTask = RunAsync(delay, timeProvider, callback);
        }

        public ValueTask DisposeAsync()
        {
            TaskCompletionSource? owner = null;
            Task disposeTask;
            lock (_sync)
            {
                if (_disposeTask is null)
                {
                    owner = new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    _disposeTask = owner.Task;
                }

                disposeTask = _disposeTask;
            }

            if (owner is not null)
            {
                _ = CompleteDisposeAsync(owner);
            }

            return new ValueTask(disposeTask);
        }

        private async Task RunAsync(
            TimeSpan delay,
            TimeProvider timeProvider,
            Action callback)
        {
            try
            {
                await Task.Delay(delay, timeProvider, _cancellation.Token).ConfigureAwait(false);
                callback();
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
            }
        }

        private async Task CompleteDisposeAsync(TaskCompletionSource completion)
        {
            var cleanupFailed = false;
            try
            {
                _cancellation.Cancel();
                await _runTask.ConfigureAwait(false);
            }
            catch
            {
                cleanupFailed = true;
            }
            finally
            {
                try
                {
                    _cancellation.Dispose();
                }
                catch
                {
                    cleanupFailed = true;
                }

                if (cleanupFailed)
                {
                    completion.TrySetException(
                        new InvalidOperationException("清理额度刷新计时器失败。"));
                }
                else
                {
                    completion.TrySetResult();
                }
            }
        }
    }
}
