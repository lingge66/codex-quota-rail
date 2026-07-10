using CodexQuotaRail.AppServer.Resilience;
using CodexQuotaRail.Core.Quotas;

namespace CodexQuotaRail.AppServer.RateLimits;

public sealed partial class RateLimitSource : IRateLimitSource
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);
    private readonly BackoffSchedule _backoff = new();
    private readonly RateLimitSourceDependencies _dependencies;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly SemaphoreSlim _refreshSignal = new(0, 1);
    private readonly object _sync = new();
    private readonly List<TaskCompletionSource> _waiters = [];
    private IRateLimitConnection? _connection;
    private Task? _disposeTask;
    private bool _hasAuthenticatedAccount;
    private bool _hasSnapshot;
    private bool _refreshQueued;
    private CancellationTokenSource? _scheduledRefresh;
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
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        QueueRefresh(completion);
        return completion.Task.WaitAsync(cancellationToken);
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

    private void QueueRefresh(TaskCompletionSource? waiter = null)
    {
        var release = false;
        lock (_sync)
        {
            if (!_started)
            {
                throw new InvalidOperationException("必须先启动额度来源。");
            }

            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
            if (waiter is not null)
            {
                _waiters.Add(waiter);
            }

            if (!_dependencies.Availability.IsPaused && !_refreshQueued)
            {
                _refreshQueued = true;
                release = true;
            }
        }

        if (release)
        {
            _refreshSignal.Release();
        }
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
                    waiters = [.. _waiters];
                    _waiters.Clear();
                }

                await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
                foreach (var waiter in waiters)
                {
                    waiter.TrySetResult();
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
}
