using CodexQuotaRail.App.Rail;
using CodexQuotaRail.Core.Quotas;
using CodexQuotaRail.Windows.Overlay;

namespace CodexQuotaRail.App.Tests;

public sealed class RailViewModelTests
{
    [Fact]
    public void ApplyCreatesTwoVisibleTracksForPrimaryAndSecondaryWindows()
    {
        var viewModel = new RailViewModel();

        viewModel.Apply(
            Live(Window("5 小时", 72), Window("本周", 41)),
            OverlayMode.ExternalRail);

        Assert.Equal(2, viewModel.Tracks.Count);
        Assert.Equal(["5 小时", "本周"], viewModel.Tracks.Select(track => track.Label));
        Assert.False(viewModel.HasSingleTrack);
    }

    [Fact]
    public void ApplyCreatesOneCenteredTrackWhenSecondaryWindowIsMissing()
    {
        var viewModel = new RailViewModel();

        viewModel.Apply(Live(Window("5 小时", 32)), OverlayMode.ExternalRail);

        Assert.Single(viewModel.Tracks);
        Assert.True(viewModel.HasSingleTrack);
        Assert.Equal(0.32, viewModel.Tracks[0].WidthFraction);
    }

    [Fact]
    public void ApplyUnavailableNeverFabricatesOneHundredPercent()
    {
        var viewModel = new RailViewModel();
        var state = new QuotaDisplayState(
            [],
            QuotaConnectionState.Unavailable,
            null,
            null);

        viewModel.Apply(state, OverlayMode.ExternalRail);

        Assert.Empty(viewModel.Tracks);
        Assert.Equal("额度暂不可用", viewModel.StatusText);
        Assert.DoesNotContain("100%", viewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyUnlimitedUsesTextWithoutFakePercent()
    {
        var viewModel = new RailViewModel();
        var unlimited = new QuotaWindowDisplay(
            "本周",
            null,
            TimeSpan.FromDays(7),
            null,
            QuotaWindowState.Unlimited);

        viewModel.Apply(Live(unlimited), OverlayMode.ExternalRail);

        var track = Assert.Single(viewModel.Tracks);
        Assert.True(track.IsUnlimited);
        Assert.Null(track.AvailablePercent);
        Assert.Equal(0, track.WidthFraction);
        Assert.Equal("无限", track.ValueText);
    }

    [Fact]
    public void ApplyCompactModeHidesLabelsButKeepsBothTracks()
    {
        var viewModel = new RailViewModel();

        viewModel.Apply(
            Live(Window("5 小时", 60), Window("本周", 20)),
            OverlayMode.CompactTitleBar);

        Assert.True(viewModel.IsCompact);
        Assert.False(viewModel.ShowLabels);
        Assert.Equal(2, viewModel.Tracks.Count);
    }

    [Fact]
    public void ApplyExhaustedStartsMarqueeOnlyWhenMotionIsAllowed()
    {
        var viewModel = new RailViewModel { ReduceMotion = false };

        viewModel.Apply(
            Live(Window("5 小时", 0, QuotaWindowState.Exhausted)),
            OverlayMode.ExternalRail);

        Assert.True(viewModel.IsMarqueeActive);
    }

    [Fact]
    public void ChangingReduceMotionImmediatelyStopsActiveMarquee()
    {
        var viewModel = new RailViewModel { ReduceMotion = false };
        viewModel.Apply(
            Live(Window("5 小时", 0, QuotaWindowState.Exhausted)),
            OverlayMode.ExternalRail);
        Assert.True(viewModel.IsMarqueeActive);

        viewModel.ReduceMotion = true;

        Assert.False(viewModel.IsMarqueeActive);
    }

    [Fact]
    public void SetViewportWidthHidesResetThenTrackTextInDegradationOrder()
    {
        var viewModel = new RailViewModel();

        viewModel.SetViewportWidth(480);
        Assert.False(viewModel.ShowResetText);
        Assert.True(viewModel.ShowTrackText);

        viewModel.SetViewportWidth(320);
        Assert.False(viewModel.ShowResetText);
        Assert.False(viewModel.ShowTrackText);
    }

    [Fact]
    public void ApplyTriggersOneShimmerOnlyWhenCrossingAWarningThreshold()
    {
        var viewModel = new RailViewModel();
        viewModel.Apply(Live(Window("5 小时", 60)), OverlayMode.ExternalRail);

        viewModel.Apply(Live(Window("5 小时", 50)), OverlayMode.ExternalRail);
        viewModel.Apply(Live(Window("5 小时", 49)), OverlayMode.ExternalRail);
        viewModel.Apply(Live(Window("5 小时", 20)), OverlayMode.ExternalRail);

        Assert.Equal(2, viewModel.ShimmerVersion);
    }

    private static QuotaDisplayState Live(params QuotaWindowDisplay[] windows) =>
        new(windows, QuotaConnectionState.Live, DateTimeOffset.UtcNow, null);

    private static QuotaWindowDisplay Window(
        string label,
        int available,
        QuotaWindowState state = QuotaWindowState.Healthy) =>
        new(label, available, TimeSpan.FromHours(5), DateTimeOffset.UtcNow.AddHours(2), state);
}
