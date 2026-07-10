namespace CodexQuotaRail.Windows.Windows;

public sealed partial class CodexWindowTracker : IDisposable
{
    private static readonly TrackedWindowEvent[] Events =
    [
        TrackedWindowEvent.Foreground,
        TrackedWindowEvent.Show,
        TrackedWindowEvent.Hide,
        TrackedWindowEvent.LocationChange,
        TrackedWindowEvent.MinimizeStart,
        TrackedWindowEvent.MinimizeEnd,
        TrackedWindowEvent.MoveSizeEnd,
    ];

    private readonly Dictionary<nint, long> _activationOrder = [];
    private readonly List<nint> _hooks = [];
    private readonly IWindowNativeApi _nativeApi;
    private readonly IWindowUpdateScheduler _scheduler;
    private readonly object _sync = new();
    private long _activationSequence;
    private TrackedWindowSnapshot? _currentSnapshot;
    private bool _disposed;
    private bool _refreshScheduled;
    private bool _started;

    public CodexWindowTracker(
        IWindowNativeApi nativeApi,
        IWindowUpdateScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        ArgumentNullException.ThrowIfNull(scheduler);
        _nativeApi = nativeApi;
        _scheduler = scheduler;
    }

    public event EventHandler<TrackedWindowSnapshot?>? SnapshotChanged;

    public TrackedWindowSnapshot? CurrentSnapshot
    {
        get
        {
            lock (_sync)
            {
                return _currentSnapshot;
            }
        }
    }

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                throw new InvalidOperationException("窗口跟踪已启动。");
            }

            _started = true;
        }

        try
        {
            foreach (var windowEvent in Events)
            {
                var hook = _nativeApi.SetWinEventHook(windowEvent, OnWindowEvent);
                if (hook == 0)
                {
                    throw new InvalidOperationException("无法监听窗口状态。");
                }

                var disposed = false;
                lock (_sync)
                {
                    disposed = _disposed;
                    if (!disposed)
                    {
                        _hooks.Add(hook);
                    }
                }

                if (disposed)
                {
                    TryUnhook(hook);
                    throw new ObjectDisposedException(nameof(CodexWindowTracker));
                }
            }

            RefreshSnapshot();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        nint[] hooks;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _refreshScheduled = false;
            hooks = [.. _hooks];
            _hooks.Clear();
        }

        foreach (var hook in hooks)
        {
            TryUnhook(hook);
        }
    }

    private void OnWindowEvent(TrackedWindowEvent windowEvent, nint handle)
    {
        if (windowEvent == TrackedWindowEvent.Foreground && handle != 0)
        {
            lock (_sync)
            {
                if (!_disposed)
                {
                    _activationOrder[handle] = ++_activationSequence;
                }
            }
        }

        ScheduleRefresh();
    }

    private void ScheduleRefresh()
    {
        lock (_sync)
        {
            if (_disposed || !_started || _refreshScheduled)
            {
                return;
            }

            _refreshScheduled = true;
        }

        try
        {
            _scheduler.Schedule(ProcessScheduledRefresh);
        }
        catch
        {
            lock (_sync)
            {
                _refreshScheduled = false;
            }
        }
    }

    private void ProcessScheduledRefresh()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _refreshScheduled = false;
        }

        RefreshSnapshot();
    }

    private void TryUnhook(nint hook)
    {
        try
        {
            _nativeApi.UnhookWinEvent(hook);
        }
        catch
        {
        }
    }
}
