using CodexQuotaRail.App.Diagnostics;
using CodexQuotaRail.App.Hosting;
using CodexQuotaRail.App.Settings;
using CodexQuotaRail.App.Tray;
using CodexQuotaRail.AppServer.RateLimits;
using CodexQuotaRail.Core.Quotas;
using CodexQuotaRail.Windows.Overlay;
using CodexQuotaRail.Windows.Startup;
using CodexQuotaRail.Windows.Windows;

namespace CodexQuotaRail.App.Tests;

internal sealed class ApplicationHostFixture : IAsyncDisposable
{
    public ApplicationHostFixture(bool hasWindow = true)
        : this()
    {
        Tracker.CurrentSnapshot = hasWindow
            ? new TrackedWindowSnapshot(
                (nint)1,
                new PixelRect(100, 100, 1000, 700),
                new PixelRect(0, 0, 1920, 1040),
                1,
                IsVisible: true,
                IsMinimized: false,
                IsMaximized: false,
                IsForeground: true)
            : null;
        Host = new ApplicationHost(
            SettingsStore,
            TrayFactory,
            Tracker,
            RateSource,
            Overlay,
            Autostart,
            Actions,
            Dispatcher,
            Log);
    }

    public List<string> Order { get; } = [];

    public FakeSettingsStore SettingsStore { get; } = new();

    public FakeTrayFactory TrayFactory { get; }

    public FakeWindowTracker Tracker { get; }

    public FakeRateSource RateSource { get; }

    public FakeOverlayPresenter Overlay { get; }

    private FakeAutostartService Autostart { get; } = new();

    private FakeApplicationActions Actions { get; } = new();

    public FakeUiDispatcher Dispatcher { get; } = new();

    private FakeApplicationLog Log { get; } = new();

    public ApplicationHost Host { get; private set; } = null!;

    public async ValueTask DisposeAsync() => await Host.ShutdownAsync();

    public static RawQuotaSnapshot LiveSnapshot(int usedPercent) =>
        new(
            new RawQuotaWindow("主额度", usedPercent, 300, 1_800_000_000, false),
            null,
            DateTimeOffset.UtcNow);

    private sealed class FakeApplicationActions : IApplicationActions
    {
        public void CheckForUpdates()
        {
        }

        public void OpenLogs()
        {
        }

        public void RequestExit()
        {
        }

        public void ShowTroubleshooting()
        {
        }
    }

    private sealed class FakeAutostartService : IAutostartService
    {
        public bool IsEnabled() => false;

        public void SetEnabled(bool enabled)
        {
        }
    }

    private sealed class FakeApplicationLog : IApplicationLog
    {
        public ValueTask WriteAsync(
            string level,
            string eventName,
            string message,
            Exception? exception = null,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    internal sealed class FakeSettingsStore : IAppSettingsStore
    {
        public List<AppSettings> Saved { get; } = [];

        public void Dispose()
        {
        }

        public ValueTask<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AppSettings());

        public ValueTask SaveAsync(
            AppSettings settings,
            CancellationToken cancellationToken = default)
        {
            Saved.Add(settings);
            return ValueTask.CompletedTask;
        }
    }

    internal sealed class FakeTrayFactory(List<string> order) : ITrayIconFactory
    {
        public FakeTrayService? Created { get; private set; }

        public ITrayIconService Create()
        {
            order.Add("tray:create");
            return Created = new FakeTrayService(order);
        }
    }

    internal sealed class FakeTrayService(List<string> order) : ITrayIconService
    {
        public event EventHandler<TrayCommandRequest>? CommandRequested;

        public int DisposeCount { get; private set; }

        public List<TrayState> States { get; } = [];

        public bool ThrowOnDispose { get; set; }

        public void Dispose()
        {
            DisposeCount++;
            order.Add("tray:dispose");
            if (ThrowOnDispose)
            {
                throw new InvalidOperationException("tray cleanup");
            }
        }

        public void Emit(TrayCommandRequest request) => CommandRequested?.Invoke(this, request);

        public void UpdateState(TrayState state) => States.Add(state);
    }

    internal sealed class FakeWindowTracker(List<string> order) : ITrackedWindowSource
    {
        public event EventHandler<TrackedWindowSnapshot?>? SnapshotChanged;

        public TrackedWindowSnapshot? CurrentSnapshot { get; set; }

        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            order.Add("tracker:dispose");
        }

        public void Start() => order.Add("tracker:start");

        public void Emit(TrackedWindowSnapshot? snapshot)
        {
            CurrentSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }
    }

    internal sealed class FakeRateSource(List<string> order) : IRateLimitSource
    {
        public event EventHandler<RawQuotaSnapshot>? SnapshotChanged;

        public event EventHandler<QuotaConnectionState>? ConnectionChanged;

        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            order.Add("source:dispose");
            return ValueTask.CompletedTask;
        }

        public void EmitConnection(QuotaConnectionState state) =>
            ConnectionChanged?.Invoke(this, state);

        public void EmitSnapshot(RawQuotaSnapshot snapshot) =>
            SnapshotChanged?.Invoke(this, snapshot);

        public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            order.Add("source:start");
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeOverlayPresenter(
        List<string> order,
        FakeUiDispatcher dispatcher) : IOverlayPresenter
    {
        public List<AppSettings> AppliedSettings { get; } = [];

        public int DisposeCount { get; private set; }

        public List<Presentation> Presentations { get; } = [];

        public void ApplySettings(AppSettings settings) => AppliedSettings.Add(settings);

        public void Dispose()
        {
            DisposeCount++;
            order.Add("overlay:dispose");
        }

        public void Present(
            QuotaDisplayState state,
            OverlayPlacement placement,
            double dpiScale)
        {
            Presentations.Add(new Presentation(state, placement, dispatcher.IsDispatching));
            order.Add("overlay:present");
        }

        public sealed record Presentation(
            QuotaDisplayState State,
            OverlayPlacement Placement,
            bool WasDispatched);
    }

    internal sealed class FakeUiDispatcher : IUiDispatcher
    {
        public bool IsDispatching { get; private set; }

        public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IsDispatching = true;
            try
            {
                action();
            }
            finally
            {
                IsDispatching = false;
            }

            return ValueTask.CompletedTask;
        }
    }

    private FakeTrayFactory CreateTrayFactory() => new(Order);

    private FakeWindowTracker CreateTracker() => new(Order);

    private FakeRateSource CreateRateSource() => new(Order);

    private FakeOverlayPresenter CreateOverlay() => new(Order, Dispatcher);

    private ApplicationHostFixture()
    {
        TrayFactory = CreateTrayFactory();
        Tracker = CreateTracker();
        RateSource = CreateRateSource();
        Overlay = CreateOverlay();
    }
}
