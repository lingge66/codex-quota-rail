namespace CodexQuotaRail.AppServer.Protocol;

internal sealed class OrderedCallbackDispatcher
{
    private const int DefaultCapacity = 64;
    private readonly int _capacity;
    private readonly Queue<Action> _pendingCallbacks = new();
    private readonly object _sync = new();
    private long _droppedCount;
    private bool _stopped;
    private TaskCompletionSource _workAvailable = NewCompletionSource();

    public OrderedCallbackDispatcher(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;

        if (ExecutionContext.IsFlowSuppressed())
        {
            _ = Task.Run(RunWorkerAsync);
            return;
        }

        using (ExecutionContext.SuppressFlow())
        {
            _ = Task.Run(RunWorkerAsync);
        }
    }

    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    public void Dispatch(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_sync)
        {
            if (_stopped)
            {
                _droppedCount++;
                return;
            }

            if (_pendingCallbacks.Count == _capacity)
            {
                _ = _pendingCallbacks.Dequeue();
                _droppedCount++;
            }

            _pendingCallbacks.Enqueue(callback);
            _workAvailable.TrySetResult();
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            _droppedCount += _pendingCallbacks.Count;
            _pendingCallbacks.Clear();
            _workAvailable.TrySetResult();
        }
    }

    private static TaskCompletionSource NewCompletionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private async Task RunWorkerAsync()
    {
        while (true)
        {
            Action? callback;
            TaskCompletionSource? workSignal;
            lock (_sync)
            {
                if (_stopped)
                {
                    return;
                }

                if (_pendingCallbacks.Count > 0)
                {
                    callback = _pendingCallbacks.Dequeue();
                    workSignal = null;
                }
                else
                {
                    callback = null;
                    workSignal = _workAvailable;
                }
            }

            if (callback is not null)
            {
                try
                {
                    callback();
                }
                catch
                {
                }

                continue;
            }

            await workSignal!.Task.ConfigureAwait(false);
            lock (_sync)
            {
                if (ReferenceEquals(_workAvailable, workSignal))
                {
                    _workAvailable = NewCompletionSource();
                }
            }
        }
    }
}
