using CodexQuotaRail.App.Hosting;

namespace CodexQuotaRail.App.Tests;

public sealed class SystemTransitionTests
{
    [Fact]
    public void DesktopSignalMapsPowerNetworkAndTaskbarEventsAndUnsubscribes()
    {
        var source = new FakeSystemEventSource();
        var signal = new DesktopTransitionSignal(source);
        var paused = 0;
        var resumed = 0;
        var network = 0;
        var taskbar = 0;
        signal.Paused += (_, _) => paused++;
        signal.Resumed += (_, _) => resumed++;
        signal.NetworkAvailable += (_, _) => network++;
        signal.TaskbarRestarted += (_, _) => taskbar++;

        source.Emit(SystemPowerTransition.Suspend);
        Assert.True(signal.IsPaused);
        source.Emit(SystemPowerTransition.Resume);
        source.EmitNetworkAvailable();
        source.EmitTaskbarRestarted();

        Assert.False(signal.IsPaused);
        Assert.Equal((1, 1, 1, 1), (paused, resumed, network, taskbar));

        signal.Dispose();
        source.Emit(SystemPowerTransition.Suspend);
        source.EmitNetworkAvailable();
        source.EmitTaskbarRestarted();
        Assert.Equal((1, 1, 1, 1), (paused, resumed, network, taskbar));
        Assert.Equal(1, source.DisposeCount);
    }

    [Fact]
    public async Task ExplorerRestartRecreatesTrayWithoutRestartingHost()
    {
        await using var fixture = new ApplicationHostFixture();
        await fixture.Host.StartAsync(CancellationToken.None);

        fixture.Transitions.EmitTaskbarRestarted();
        await fixture.Host.WhenIdleAsync();

        Assert.Equal(1, fixture.TrayFactory.Created!.RecreateCount);
        Assert.Equal(0, fixture.TrayFactory.Created.DisposeCount);
    }

    private sealed class FakeSystemEventSource : ISystemEventSource
    {
        public event EventHandler<SystemPowerTransition>? PowerChanged;

        public event EventHandler? NetworkAvailable;

        public event EventHandler? TaskbarRestarted;

        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;

        public void Emit(SystemPowerTransition transition) =>
            PowerChanged?.Invoke(this, transition);

        public void EmitNetworkAvailable() => NetworkAvailable?.Invoke(this, EventArgs.Empty);

        public void EmitTaskbarRestarted() => TaskbarRestarted?.Invoke(this, EventArgs.Empty);
    }
}
