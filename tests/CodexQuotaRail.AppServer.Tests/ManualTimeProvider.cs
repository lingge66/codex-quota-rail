namespace CodexQuotaRail.AppServer.Tests;

internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _sync = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _utcNow = DateTimeOffset.UnixEpoch;

    public Action? BeforeTimerCallback { get; set; }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_sync)
        {
            return _utcNow;
        }
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new ManualTimer(this, callback, state, dueTime, period);
        lock (_sync)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    public void Advance(TimeSpan amount)
    {
        List<(TimerCallback Callback, object? State)> callbacks;
        lock (_sync)
        {
            _utcNow += amount;
            callbacks = _timers
                .SelectMany(timer => timer.TakeDueCallbacks(_utcNow))
                .ToList();
        }

        foreach (var (callback, state) in callbacks)
        {
            BeforeTimerCallback?.Invoke();
            callback(state);
        }
    }

    private void Remove(ManualTimer timer)
    {
        lock (_sync)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly TimerCallback _callback;
        private readonly ManualTimeProvider _owner;
        private readonly object? _state;
        private DateTimeOffset? _dueAt;
        private bool _disposed;
        private TimeSpan _period;

        public ManualTimer(
            ManualTimeProvider owner,
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
            _period = period;
            _dueAt = DueAt(owner.GetUtcNow(), dueTime);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_owner._sync)
            {
                if (_disposed)
                {
                    return false;
                }

                _period = period;
                _dueAt = DueAt(_owner._utcNow, dueTime);
                return true;
            }
        }

        public void Dispose()
        {
            lock (_owner._sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            _owner.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public IEnumerable<(TimerCallback Callback, object? State)> TakeDueCallbacks(
            DateTimeOffset now)
        {
            if (_disposed || _dueAt is null || _dueAt > now)
            {
                yield break;
            }

            yield return (_callback, _state);
            _dueAt = _period == Timeout.InfiniteTimeSpan
                ? null
                : now + _period;
        }

        private static DateTimeOffset? DueAt(DateTimeOffset now, TimeSpan dueTime) =>
            dueTime == Timeout.InfiniteTimeSpan ? null : now + dueTime;
    }
}
