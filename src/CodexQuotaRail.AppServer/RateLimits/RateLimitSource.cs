using CodexQuotaRail.AppServer.Resilience;
using CodexQuotaRail.AppServer.Protocol;
using CodexQuotaRail.Core.Quotas;

namespace CodexQuotaRail.AppServer.RateLimits;

public sealed partial class RateLimitSource : IRateLimitSource
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);
    private readonly BackoffSchedule _backoff = new();
    private readonly OrderedCallbackDispatcher _callbackDispatcher = new();
    private readonly RateLimitSourceDependencies _dependencies;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly SemaphoreSlim _refreshSignal = new(0, 1);
    private readonly object _sync = new();
    private readonly Dictionary<long, TaskCompletionSource> _waiters = [];
    private IRateLimitConnection? _connection;
    private Task? _disposeTask;
    private bool _hasAuthenticatedAccount;
    private bool _hasSnapshot;
    private bool _refreshQueued;
    private long _nextWaiterId;
    private ScheduledRefresh? _scheduledRefresh;
    private bool _started;
    private Task? _workerTask;

    public RateLimitSource(RateLimitSourceDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(dependencies.ExecutableResolver);
        ArgumentNullException.ThrowIfNull(dependencies.ConnectionFactory);
        ArgumentNullException.ThrowIfNull(dependencies.Availability);
        ArgumentNullException.ThrowIfNull(dependencies.TimeProvider);
        ArgumentNullException.ThrowIfNull(dependencies.ClientVersion);
        _dependencies = dependencies;
    }

    public event EventHandler<RawQuotaSnapshot>? SnapshotChanged;

    public event EventHandler<QuotaConnectionState>? ConnectionChanged;

    public int PendingWaiterCount
    {
        get
        {
            lock (_sync)
            {
                return _waiters.Count;
            }
        }
    }

    public long DroppedCallbackCount => _callbackDispatcher.DroppedCount;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
            if (_started)
            {
                throw new InvalidOperationException("额度来源只能启动一次。");
            }

            _started = true;
            SubscribeAvailability();
            _workerTask = Task.Run(
                () => RunWorkerAsync(_lifetimeCancellation.Token),
                CancellationToken.None);
        }

        return RefreshAsync(cancellationToken);
    }

    public Task RefreshAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = Interlocked.Increment(ref _nextWaiterId);
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration registration;
        var release = false;
        lock (_sync)
        {
            EnsureRefreshCanBeQueuedLocked();
            _waiters.Add(id, completion);
            registration = cancellationToken.UnsafeRegister(
                static state =>
                {
                    var cancellation = (WaiterCancellationState)state!;
                    cancellation.Source.CancelWaiter(cancellation.Id);
                    cancellation.Completion.TrySetCanceled(cancellation.Token);
                },
                new WaiterCancellationState(
                    this,
                    id,
                    completion,
                    cancellationToken));
            release = QueueRefreshLocked();
        }

        if (release)
        {
            _refreshSignal.Release();
        }

        return AwaitWaiterAsync(completion.Task, registration);
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
            _callbackDispatcher.Stop();
            _ = CompleteDisposeAsync(owner);
        }

        return new ValueTask(disposeTask);
    }

    private void QueueRefresh()
    {
        var release = false;
        lock (_sync)
        {
            EnsureRefreshCanBeQueuedLocked();
            release = QueueRefreshLocked();
        }

        if (release)
        {
            _refreshSignal.Release();
        }
    }

    private bool QueueRefreshLocked()
    {
        if (_dependencies.Availability.IsPaused || _refreshQueued)
        {
            return false;
        }

        _refreshQueued = true;
        return true;
    }

    private void EnsureRefreshCanBeQueuedLocked()
    {
        if (!_started)
        {
            throw new InvalidOperationException("必须先启动额度来源。");
        }

        ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
    }

    private async Task RunWorkerAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await _refreshSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
                TaskCompletionSource[] waiters;
                lock (_sync)
                {
                    _refreshQueued = false;
                    waiters = [.. _waiters.Values];
                    _waiters.Clear();
                }

                Exception? batchError = null;
                try
                {
                    await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        batchError = new ObjectDisposedException(nameof(RateLimitSource));
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    batchError = new ObjectDisposedException(nameof(RateLimitSource));
                }
                catch
                {
                    batchError = new AppServerProtocolException("刷新额度失败。");
                }
                finally
                {
                    CompleteBatchWaiters(waiters, batchError);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            CompletePendingWaiters();
        }
    }

    private void CancelWaiter(long id)
    {
        lock (_sync)
        {
            _waiters.Remove(id);
        }
    }

    private static async Task AwaitWaiterAsync(
        Task waiter,
        CancellationTokenRegistration registration)
    {
        try
        {
            await waiter.ConfigureAwait(false);
        }
        finally
        {
            registration.Dispose();
        }
    }

    private static void CompleteBatchWaiters(
        IReadOnlyList<TaskCompletionSource> waiters,
        Exception? error)
    {
        foreach (var waiter in waiters)
        {
            if (error is null)
            {
                waiter.TrySetResult();
            }
            else
            {
                waiter.TrySetException(error);
            }
        }
    }

    private sealed record WaiterCancellationState(
        RateLimitSource Source,
        long Id,
        TaskCompletionSource Completion,
        CancellationToken Token);
}
