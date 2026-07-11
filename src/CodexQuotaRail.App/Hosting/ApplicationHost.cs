using CodexQuotaRail.App.Diagnostics;
using CodexQuotaRail.App.Settings;
using CodexQuotaRail.App.Tray;
using CodexQuotaRail.AppServer.RateLimits;
using CodexQuotaRail.Core.Quotas;
using CodexQuotaRail.Windows.Overlay;
using CodexQuotaRail.Windows.Startup;
using CodexQuotaRail.Windows.Windows;

namespace CodexQuotaRail.App.Hosting;

public sealed partial class ApplicationHost : IAsyncDisposable
{
    private static readonly OverlayPlacement HiddenPlacement = new(
        new PixelRect(0, 0, 0, 0),
        OverlayMode.Hidden,
        0);
    private readonly IAccessibilitySettings _accessibility;
    private readonly IApplicationActions _actions;
    private readonly IAutostartService _autostart;
    private readonly IUiDispatcher _dispatcher;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly IApplicationLog _log;
    private readonly IOverlayPresenter _overlay;
    private readonly IRateLimitSource _rateSource;
    private readonly IAppSettingsStore _settingsStore;
    private readonly object _sync = new();
    private readonly ITrayIconFactory _trayFactory;
    private readonly IDesktopTransitionSignal _transitions;
    private readonly ITrackedWindowSource _windowTracker;
    private QuotaDisplayState _display = QuotaDisplayState.Waiting("正在连接 Codex");
    private Task _eventTail = Task.CompletedTask;
    private bool _eventsEnabled;
    private AppSettings _settings = new();
    private Task? _shutdownTask;
    private bool _started;
    private ITrayIconService? _tray;
    private TrackedWindowSnapshot? _window;

    public ApplicationHost(
        IAppSettingsStore settingsStore,
        ITrayIconFactory trayFactory,
        ITrackedWindowSource windowTracker,
        IRateLimitSource rateSource,
        IDesktopTransitionSignal transitions,
        IOverlayPresenter overlay,
        IAutostartService autostart,
        IApplicationActions actions,
        IUiDispatcher dispatcher,
        IAccessibilitySettings accessibility,
        IApplicationLog log)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _trayFactory = trayFactory ?? throw new ArgumentNullException(nameof(trayFactory));
        _windowTracker = windowTracker ?? throw new ArgumentNullException(nameof(windowTracker));
        _rateSource = rateSource ?? throw new ArgumentNullException(nameof(rateSource));
        _transitions = transitions ?? throw new ArgumentNullException(nameof(transitions));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _autostart = autostart ?? throw new ArgumentNullException(nameof(autostart));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _accessibility = accessibility ?? throw new ArgumentNullException(nameof(accessibility));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_shutdownTask is not null, this);
            if (_started)
            {
                throw new InvalidOperationException("应用主机只能启动一次。");
            }

            _started = true;
        }

        _settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (!_settings.ReduceMotionConfigured)
        {
            _settings = _settings with
            {
                ReduceMotion = !_accessibility.ClientAreaAnimationEnabled,
            };
        }
        await _dispatcher.InvokeAsync(
            StartUiAndWindowTracking,
            cancellationToken).ConfigureAwait(false);
        await _rateSource.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task WhenIdleAsync()
    {
        lock (_sync)
        {
            return _eventTail;
        }
    }

    public Task RequestRefreshAsync(CancellationToken cancellationToken = default) =>
        _rateSource.RefreshAsync(cancellationToken);

    public Task ShutdownAsync()
    {
        TaskCompletionSource? owner = null;
        Task task;
        lock (_sync)
        {
            if (_shutdownTask is null)
            {
                owner = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _shutdownTask = owner.Task;
                _eventsEnabled = false;
            }

            task = _shutdownTask;
        }

        if (owner is not null)
        {
            _ = CompleteShutdownAsync(owner);
        }

        return task;
    }

    public ValueTask DisposeAsync() => new(ShutdownAsync());

    private void StartUiAndWindowTracking()
    {
        _tray = _trayFactory.Create();
        _tray.CommandRequested += OnTrayCommandRequested;
        _overlay.ApplySettings(_settings);
        _windowTracker.Start();
        _window = _windowTracker.CurrentSnapshot;
        _windowTracker.SnapshotChanged += OnWindowSnapshotChanged;
        _rateSource.SnapshotChanged += OnRateSnapshotChanged;
        _rateSource.ConnectionChanged += OnConnectionChanged;
        _transitions.TaskbarRestarted += OnTaskbarRestarted;
        lock (_sync)
        {
            _eventsEnabled = true;
        }

        RenderCurrent();
    }

    private void RenderCurrent()
    {
        var placement = _window is null
            ? HiddenPlacement
            : OverlayPlacementCalculator.Calculate(_window);
        _overlay.Present(
            _display,
            placement,
            _window?.DpiScale ?? 1,
            _window?.Handle ?? 0);
        _tray?.UpdateState(CreateTrayState());
    }

    private TrayState CreateTrayState() => new(
        StatusText(_display),
        _display.UpdatedAt,
        _settings.FollowPaused,
        _settings.Theme,
        _settings.ReduceMotion,
        _settings.StartWithWindows);

    private static string StatusText(QuotaDisplayState state) => state.Connection switch
    {
        QuotaConnectionState.Connecting => state.Message ?? "正在连接 Codex",
        QuotaConnectionState.Live => "额度已更新",
        QuotaConnectionState.Stale => "连接暂时中断",
        QuotaConnectionState.AuthenticationRequired => "请先登录 Codex",
        QuotaConnectionState.Unsupported => "当前 Codex 版本暂不支持",
        _ => "Codex 暂不可用",
    };

    private async Task CompleteShutdownAsync(TaskCompletionSource completion)
    {
        try
        {
            await RunCleanupAsync(
                "unsubscribe_events",
                () =>
                {
                    UnsubscribeEvents();
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            await RunCleanupAsync(
                "cancel_events",
                _lifetime.CancelAsync).ConfigureAwait(false);
            await RunCleanupAsync(
                "drain_events",
                WhenIdleAsync).ConfigureAwait(false);
            await RunCleanupAsync(
                "dispose_tray",
                () => DisposeOnUiAsync(() => _tray?.Dispose())).ConfigureAwait(false);
            await RunCleanupAsync(
                "dispose_tracker",
                () =>
                {
                    _windowTracker.Dispose();
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            await RunCleanupAsync(
                "dispose_rate_source",
                () => _rateSource.DisposeAsync().AsTask()).ConfigureAwait(false);
            await RunCleanupAsync(
                "dispose_transitions",
                () => DisposeOnUiAsync(_transitions.Dispose)).ConfigureAwait(false);
            await RunCleanupAsync(
                "dispose_overlay",
                () => DisposeOnUiAsync(_overlay.Dispose)).ConfigureAwait(false);
            await RunCleanupAsync(
                "dispose_settings",
                () =>
                {
                    _settingsStore.Dispose();
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            await RunCleanupAsync(
                "dispose_log",
                () =>
                {
                    if (_log is IDisposable disposableLog)
                    {
                        disposableLog.Dispose();
                    }

                    return Task.CompletedTask;
                }).ConfigureAwait(false);
        }
        finally
        {
            _lifetime.Dispose();
            completion.TrySetResult();
        }
    }

    private void UnsubscribeEvents()
    {
        if (_tray is not null)
        {
            _tray.CommandRequested -= OnTrayCommandRequested;
        }

        _windowTracker.SnapshotChanged -= OnWindowSnapshotChanged;
        _rateSource.SnapshotChanged -= OnRateSnapshotChanged;
        _rateSource.ConnectionChanged -= OnConnectionChanged;
        _transitions.TaskbarRestarted -= OnTaskbarRestarted;
    }

    private async Task DisposeOnUiAsync(Action action)
    {
        await _dispatcher.InvokeAsync(action, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task RunCleanupAsync(string eventName, Func<Task> cleanup)
    {
        try
        {
            await cleanup().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            await SafeLogAsync(eventName, error).ConfigureAwait(false);
        }
    }
}
