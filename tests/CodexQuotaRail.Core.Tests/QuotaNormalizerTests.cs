using CodexQuotaRail.Core.Quotas;

namespace CodexQuotaRail.Core.Tests;

public sealed class QuotaNormalizerTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(68, 32)]
    [InlineData(100, 0)]
    [InlineData(-5, 100)]
    [InlineData(120, 0)]
    public void ConvertsUsedToAvailable(int used, int expected)
    {
        var source = new RawQuotaWindow("5 小时", used, 300, 1_800_000_000, false);
        var result = QuotaNormalizer.NormalizeWindow(source);

        Assert.Equal(expected, result.AvailablePercent);
    }

    [Fact]
    public void ExtremeNegativeUsedPercentIsClampedBeforeConverting()
    {
        var source = new RawQuotaWindow("5 小时", int.MinValue, 300, null, false);

        var result = QuotaNormalizer.NormalizeWindow(source);

        Assert.Equal(100, result.AvailablePercent);
        Assert.Equal(QuotaWindowState.Healthy, result.State);
    }

    [Fact]
    public void MissingUsedPercentIsUnavailableNotFull()
    {
        var source = new RawQuotaWindow("5 小时", null, 300, null, false);
        var result = QuotaNormalizer.NormalizeWindow(source);

        Assert.Equal(QuotaWindowState.Unavailable, result.State);
        Assert.Null(result.AvailablePercent);
    }

    [Fact]
    public void OmitsMissingSecondaryWindow()
    {
        var source = new RawQuotaSnapshot(
            new RawQuotaWindow("5 小时", 40, 300, null, false),
            null,
            DateTimeOffset.UnixEpoch);

        var result = QuotaNormalizer.Normalize(source);

        Assert.Single(result.Windows);
    }

    [Fact]
    public void WaitingCreatesConnectingStateWithoutQuotaWindows()
    {
        var result = QuotaDisplayState.Waiting("正在连接 Codex");

        Assert.Empty(result.Windows);
        Assert.Equal(QuotaConnectionState.Connecting, result.Connection);
        Assert.Null(result.UpdatedAt);
        Assert.Equal("正在连接 Codex", result.Message);
    }

    [Fact]
    public void NormalizesBothWindowsInSourceOrderAndMarksSnapshotLive()
    {
        var receivedAt = DateTimeOffset.FromUnixTimeSeconds(1_800_000_100);
        var source = new RawQuotaSnapshot(
            new RawQuotaWindow("5 小时", 40, 300, null, false),
            new RawQuotaWindow("本周", 20, 10_080, null, false),
            receivedAt);

        var result = QuotaNormalizer.Normalize(source);

        Assert.Collection(
            result.Windows,
            primary => Assert.Equal("5 小时", primary.Label),
            secondary => Assert.Equal("本周", secondary.Label));
        Assert.Equal(QuotaConnectionState.Live, result.Connection);
        Assert.Equal(receivedAt, result.UpdatedAt);
        Assert.Null(result.Message);
    }

    [Theory]
    [InlineData(49, QuotaWindowState.Healthy)]
    [InlineData(50, QuotaWindowState.Notice)]
    [InlineData(79, QuotaWindowState.Notice)]
    [InlineData(80, QuotaWindowState.Critical)]
    [InlineData(99, QuotaWindowState.Critical)]
    [InlineData(100, QuotaWindowState.Exhausted)]
    public void MapsAvailablePercentToWindowState(int used, QuotaWindowState expected)
    {
        var source = new RawQuotaWindow("5 小时", used, 300, null, false);

        var result = QuotaNormalizer.NormalizeWindow(source);

        Assert.Equal(expected, result.State);
    }

    [Fact]
    public void ConvertsDurationAndResetTimestamp()
    {
        var source = new RawQuotaWindow("5 小时", 40, 300, 1_800_000_000, false);

        var result = QuotaNormalizer.NormalizeWindow(source);

        Assert.Equal(TimeSpan.FromMinutes(300), result.WindowDuration);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000), result.ResetsAt);
    }

    [Fact]
    public void UnlimitedWindowDoesNotPretendToHaveAPercentage()
    {
        var source = new RawQuotaWindow("5 小时", 80, 300, 1_800_000_000, true);

        var result = QuotaNormalizer.NormalizeWindow(source);

        Assert.Equal(QuotaWindowState.Unlimited, result.State);
        Assert.Null(result.AvailablePercent);
        Assert.Equal(TimeSpan.FromMinutes(300), result.WindowDuration);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000), result.ResetsAt);
    }
}
