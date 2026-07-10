namespace CodexQuotaRail.AppServer.Protocol;

internal enum CallbackCategory
{
    Normal,
    ProtocolError,
}

internal sealed class OrderedCallbackDispatcher
{
    private const int DefaultCapacity = 64;
    private readonly int _capacity;
    private readonly LinkedList<CallbackWorkItem> _pendingCallbacks = new();
    private readonly object _sync = new();
    private long _droppedCount;
    private long _droppedProtocolErrorCount;
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

    public long DroppedProtocolErrorCount =>
        Interlocked.Read(ref _droppedProtocolErrorCount);

    public void Dispatch(
        Action callback,
        CallbackCategory category = CallbackCategory.Normal)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_sync)
        {
            if (_stopped)
            {
                RecordDropped(category, 1);
                return;
            }

            if (_pendingCallbacks.Count == _capacity)
            {
                var oldestNormal = FindOldestNormal();
                if (oldestNormal is not null)
                {
                    _pendingCallbacks.Remove(oldestNormal);
                    RecordDropped(CallbackCategory.Normal, 1);
                }
                else if (category == CallbackCategory.ProtocolError)
                {
                    _pendingCallbacks.RemoveFirst();
                    RecordDropped(CallbackCategory.ProtocolError, 1);
                }
                else
                {
                    RecordDropped(CallbackCategory.Normal, 1);
                    return;
                }
            }

            _pendingCallbacks.AddLast(new CallbackWorkItem(callback, category));
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
            var droppedNormalCount = 0;
            var droppedProtocolErrorCount = 0;
            foreach (var workItem in _pendingCallbacks)
            {
                if (workItem.Category == CallbackCategory.ProtocolError)
                {
                    droppedProtocolErrorCount++;
                }
                else
                {
                    droppedNormalCount++;
                }
            }

            RecordDropped(CallbackCategory.Normal, droppedNormalCount);
            RecordDropped(CallbackCategory.ProtocolError, droppedProtocolErrorCount);
            _pendingCallbacks.Clear();
            _workAvailable.TrySetResult();
        }
    }

    private static TaskCompletionSource NewCompletionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private LinkedListNode<CallbackWorkItem>? FindOldestNormal()
    {
        for (var node = _pendingCallbacks.First; node is not null; node = node.Next)
        {
            if (node.Value.Category == CallbackCategory.Normal)
            {
                return node;
            }
        }

        return null;
    }

    private void RecordDropped(CallbackCategory category, int count)
    {
        if (count == 0)
        {
            return;
        }

        if (category == CallbackCategory.ProtocolError)
        {
            Interlocked.Add(ref _droppedProtocolErrorCount, count);
            return;
        }

        Interlocked.Add(ref _droppedCount, count);
    }

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
                    callback = _pendingCallbacks.First!.Value.Callback;
                    _pendingCallbacks.RemoveFirst();
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

    private readonly record struct CallbackWorkItem(
        Action Callback,
        CallbackCategory Category);
}
