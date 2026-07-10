namespace CodexQuotaRail.AppServer.Protocol;

internal sealed class OrderedCallbackDispatcher
{
    private readonly object _sync = new();
    private bool _stopped;
    private Task _tail = Task.CompletedTask;

    public void Dispatch(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        lock (_sync)
        {
            if (_stopped)
            {
                return;
            }

            if (ExecutionContext.IsFlowSuppressed())
            {
                Schedule(callback);
                return;
            }

            using (ExecutionContext.SuppressFlow())
            {
                Schedule(callback);
            }
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            _stopped = true;
        }
    }

    private void Schedule(Action callback)
    {
        _tail = _tail.ContinueWith(
            static (_, state) => ((CallbackWorkItem)state!).Invoke(),
            new CallbackWorkItem(this, callback),
            CancellationToken.None,
            TaskContinuationOptions.DenyChildAttach,
            TaskScheduler.Default);
    }

    private void Invoke(Action callback)
    {
        lock (_sync)
        {
            if (_stopped)
            {
                return;
            }
        }

        try
        {
            callback();
        }
        catch
        {
        }
    }

    private sealed record CallbackWorkItem(OrderedCallbackDispatcher Owner, Action Callback)
    {
        public void Invoke() => Owner.Invoke(Callback);
    }
}
