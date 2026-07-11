using CodexQuotaRail.App.Hosting;
using CodexQuotaRail.App.Settings;
using CodexQuotaRail.App.Tray;
using CodexQuotaRail.Core.Quotas;
using CodexQuotaRail.Windows.Overlay;

namespace CodexQuotaRail.App.Tests;

public sealed class ApplicationHostTests
{
    [Fact]
    public async Task StartRendersWaitingBeforeRateSourceStarts()
    {
        await using var fixture = new ApplicationHostFixture();

        await fixture.Host.StartAsync(CancellationToken.None);

        var first = Assert.Single(fixture.Overlay.Presentations);
        Assert.Equal(QuotaConnectionState.Connecting, first.State.Connection);
        Assert.Equal("正在连接 Codex", first.State.Message);
        Assert.Equal((nint)1, first.OwnerHandle);
        Assert.True(first.WasDispatched);
        Assert.True(
            fixture.Order.IndexOf("overlay:present") <
            fixture.Order.IndexOf("source:start"));
    }

    [Fact]
    public async Task FirstSnapshotIsNormalizedAndRenderedOnUiDispatcher()
    {
        await using var fixture = new ApplicationHostFixture();
        await fixture.Host.StartAsync(CancellationToken.None);

        fixture.RateSource.EmitSnapshot(
            new RawQuotaSnapshot(
                new RawQuotaWindow("5 小时", 68, 300, 1_800_000_000, false),
                new RawQuotaWindow("本周", 41, 10_080, 1_800_100_000, false),
                DateTimeOffset.UtcNow));
        await fixture.Host.WhenIdleAsync();

        var rendered = fixture.Overlay.Presentations[^1];
        Assert.Equal([32, 59], rendered.State.Windows.Select(window => window.AvailablePercent));
        Assert.Equal(QuotaConnectionState.Live, rendered.State.Connection);
        Assert.True(rendered.WasDispatched);
        Assert.Equal(OverlayMode.ExternalRail, rendered.Placement.Mode);
    }

    [Fact]
    public async Task StaleConnectionKeepsLastQuotaWindows()
    {
        await using var fixture = new ApplicationHostFixture();
        await fixture.Host.StartAsync(CancellationToken.None);
        fixture.RateSource.EmitSnapshot(ApplicationHostFixture.LiveSnapshot(25));
        await fixture.Host.WhenIdleAsync();

        fixture.RateSource.EmitConnection(QuotaConnectionState.Stale);
        await fixture.Host.WhenIdleAsync();

        var rendered = fixture.Overlay.Presentations[^1].State;
        Assert.Equal(QuotaConnectionState.Stale, rendered.Connection);
        Assert.Equal(75, Assert.Single(rendered.Windows).AvailablePercent);
        Assert.Equal("连接暂时中断，显示最近额度", rendered.Message);
    }

    [Fact]
    public async Task MissingCodexWindowHidesOverlayAndKeepsTrayAlive()
    {
        await using var fixture = new ApplicationHostFixture(hasWindow: false);
        await fixture.Host.StartAsync(CancellationToken.None);
        fixture.RateSource.EmitSnapshot(ApplicationHostFixture.LiveSnapshot(10));
        await fixture.Host.WhenIdleAsync();

        Assert.Equal(OverlayMode.Hidden, fixture.Overlay.Presentations[^1].Placement.Mode);
        Assert.NotNull(fixture.TrayFactory.Created);
        Assert.Equal(0, fixture.TrayFactory.Created!.DisposeCount);
    }

    [Fact]
    public async Task TraySettingChangePersistsAndAppliesImmediately()
    {
        await using var fixture = new ApplicationHostFixture();
        await fixture.Host.StartAsync(CancellationToken.None);

        fixture.TrayFactory.Created!.Emit(
            new TrayCommandRequest(
                TrayCommand.SetReduceMotion,
                BooleanValue: true));
        await fixture.Host.WhenIdleAsync();

        Assert.True(Assert.Single(fixture.SettingsStore.Saved).ReduceMotion);
        Assert.True(fixture.Overlay.AppliedSettings[^1].ReduceMotion);
        Assert.True(fixture.TrayFactory.Created.States[^1].ReduceMotion);
    }

    [Fact]
    public async Task EveryNonSettingTrayCommandReachesItsApplicationAction()
    {
        await using var fixture = new ApplicationHostFixture();
        await fixture.Host.StartAsync(CancellationToken.None);

        fixture.TrayFactory.Created!.Emit(new TrayCommandRequest(TrayCommand.Refresh));
        fixture.TrayFactory.Created.Emit(new TrayCommandRequest(TrayCommand.CheckUpdates));
        fixture.TrayFactory.Created.Emit(new TrayCommandRequest(TrayCommand.OpenLogs));
        fixture.TrayFactory.Created.Emit(new TrayCommandRequest(TrayCommand.Troubleshoot));
        fixture.TrayFactory.Created.Emit(new TrayCommandRequest(TrayCommand.OpenLingGeWebsite));
        fixture.TrayFactory.Created.Emit(new TrayCommandRequest(TrayCommand.Exit));
        await fixture.Host.WhenIdleAsync();

        Assert.Equal(1, fixture.RateSource.RefreshCount);
        Assert.Equal(
            [
                TrayCommand.CheckUpdates,
                TrayCommand.OpenLogs,
                TrayCommand.Troubleshoot,
                TrayCommand.OpenLingGeWebsite,
                TrayCommand.Exit,
            ],
            fixture.Actions.Invoked);
    }

    [Fact]
    public async Task AutostartClickUpdatesWindowsAndPersistsTheSetting()
    {
        await using var fixture = new ApplicationHostFixture();
        await fixture.Host.StartAsync(CancellationToken.None);

        fixture.TrayFactory.Created!.Emit(
            new TrayCommandRequest(TrayCommand.SetAutostart, BooleanValue: true));
        await fixture.Host.WhenIdleAsync();

        Assert.Equal([true], fixture.Autostart.SetValues);
        Assert.True(Assert.Single(fixture.SettingsStore.Saved).StartWithWindows);
    }

    [Fact]
    public async Task ShutdownIsIdempotentAndUsesDeclaredReverseDependencyOrder()
    {
        await using var fixture = new ApplicationHostFixture();
        await fixture.Host.StartAsync(CancellationToken.None);

        await Task.WhenAll(fixture.Host.ShutdownAsync(), fixture.Host.ShutdownAsync());

        Assert.Equal(
            [
                "tray:dispose",
                "tracker:dispose",
                "source:dispose",
                "transitions:dispose",
                "overlay:dispose",
            ],
            fixture.Order.Where(item => item.EndsWith(":dispose", StringComparison.Ordinal)));
        Assert.Equal(1, fixture.TrayFactory.Created!.DisposeCount);
        Assert.Equal(1, fixture.Tracker.DisposeCount);
        Assert.Equal(1, fixture.RateSource.DisposeCount);
        Assert.Equal(1, fixture.Overlay.DisposeCount);
    }

    [Fact]
    public async Task ShutdownContinuesAfterTrayCleanupFailure()
    {
        await using var fixture = new ApplicationHostFixture();
        await fixture.Host.StartAsync(CancellationToken.None);
        fixture.TrayFactory.Created!.ThrowOnDispose = true;

        await fixture.Host.ShutdownAsync();

        Assert.Equal(1, fixture.TrayFactory.Created.DisposeCount);
        Assert.Equal(1, fixture.Tracker.DisposeCount);
        Assert.Equal(1, fixture.RateSource.DisposeCount);
        Assert.Equal(1, fixture.Overlay.DisposeCount);
    }
}
