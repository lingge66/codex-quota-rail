using System.Text.Json;
using CodexQuotaRail.AppServer.Discovery;
using CodexQuotaRail.AppServer.Protocol;
using CodexQuotaRail.AppServer.RateLimits;
using CodexQuotaRail.Core.Quotas;

namespace CodexQuotaRail.AppServer.Tests;

public sealed class RateLimitSourceTests
{
    [Fact]
    public async Task StartReadsAccountAndRateLimitsAndIsolatesSnapshotSubscribers()
    {
        // Given
        await using var fixture = SourceFixture.Create();
        fixture.Source.SnapshotChanged += (_, _) => throw new InvalidOperationException("consumer");
        var snapshotReceived = new TaskCompletionSource<RawQuotaSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Source.SnapshotChanged += (_, snapshot) => snapshotReceived.TrySetResult(snapshot);

        // When
        await fixture.Source.StartAsync(fixture.CancellationToken);
        var snapshot = await snapshotReceived.Task.WaitAsync(fixture.CancellationToken);
        await fixture.WaitForConnectionStateAsync(QuotaConnectionState.Live);

        // Then
        Assert.Equal(["account/read", "account/rateLimits/read"], fixture.Connection.RequestedMethods);
        Assert.Equal(68, snapshot.Primary?.UsedPercent);
        Assert.Equal(QuotaConnectionState.Live, fixture.ConnectionStates.Last());
        var launch = Assert.Single(fixture.Factory.LaunchSpecs);
        Assert.Equal(@"C:\tools\codex.exe", launch.FileName);
        Assert.Equal(["app-server", "--listen", "stdio://"], launch.Arguments);
    }

    [Fact]
    public async Task NotificationIgnoresPayloadAndTriggersOneFreshRead()
    {
        // Given
        await using var fixture = SourceFixture.Create();
        var initial = fixture.NextSnapshot();
        await fixture.Source.StartAsync(fixture.CancellationToken);
        await initial.WaitAsync(fixture.CancellationToken);
        fixture.Connection.RateLimitsResult = JsonFixture.RateLimits(20);
        var refreshed = fixture.NextSnapshot();

        // When
        fixture.Connection.RaiseNotification(
            "account/rateLimits/updated",
            JsonFixture.Element("""{ "primary": { "usedPercent": 99 } }"""));
        var snapshot = await refreshed.WaitAsync(fixture.CancellationToken);

        // Then
        Assert.Equal(20, snapshot.Primary?.UsedPercent);
        Assert.Equal(2, fixture.Connection.RateLimitReadCount);
    }

    [Fact]
    public async Task SuccessfulSnapshotSchedulesRefreshAfterSixtySeconds()
    {
        // Given
        await using var fixture = SourceFixture.Create();
        var initial = fixture.NextSnapshot();
        await fixture.Source.StartAsync(fixture.CancellationToken);
        await initial.WaitAsync(fixture.CancellationToken);
        fixture.Time.Advance(TimeSpan.FromSeconds(59));

        // When
        var beforeDue = fixture.Connection.RateLimitReadCount;
        var refreshed = fixture.NextSnapshot();
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        await refreshed.WaitAsync(fixture.CancellationToken);

        // Then
        Assert.Equal(1, beforeDue);
        Assert.Equal(2, fixture.Connection.RateLimitReadCount);
    }

    [Fact]
    public async Task FailureUsesBackoffAndSuccessfulSnapshotResetsIt()
    {
        // Given
        var failedConnection = FakeRateLimitConnection.StartFailure();
        var recoveredConnection = FakeRateLimitConnection.Valid();
        var replacementConnection = FakeRateLimitConnection.Valid();
        await using var fixture = SourceFixture.Create(
            failedConnection,
            recoveredConnection,
            replacementConnection);
        await fixture.Source.StartAsync(fixture.CancellationToken);

        // When
        var recovered = fixture.NextSnapshot();
        fixture.Time.Advance(TimeSpan.FromSeconds(2));
        await recovered.WaitAsync(fixture.CancellationToken);
        recoveredConnection.NextRateLimitException = new IOException("private raw failure");
        var stale = fixture.WaitForConnectionStateAsync(QuotaConnectionState.Stale);
        await fixture.Source.RefreshAsync(fixture.CancellationToken);
        await stale;
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        var beforeResetDelay = fixture.Factory.CreateCount;
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        await fixture.WaitForFactoryCountAsync(3);

        // Then
        Assert.Equal(2, beforeResetDelay);
        Assert.True(recoveredConnection.IsDisposed);
        Assert.Equal(3, fixture.Factory.CreateCount);
        Assert.Contains(QuotaConnectionState.Stale, fixture.ConnectionStates);
    }

    [Fact]
    public async Task NetworkRestoreResetsBackoffBeforeImmediateRefresh()
    {
        var first = FakeRateLimitConnection.StartFailure();
        var second = FakeRateLimitConnection.StartFailure();
        var third = FakeRateLimitConnection.StartFailure();
        var fourth = FakeRateLimitConnection.Valid();
        await using var fixture = SourceFixture.Create(first, second, third, fourth);
        await fixture.Source.StartAsync(fixture.CancellationToken);
        fixture.Time.Advance(TimeSpan.FromSeconds(2));
        await fixture.WaitForFactoryCountAsync(2);

        fixture.Availability.SignalNetworkAvailable();
        await fixture.WaitForFactoryCountAsync(3);
        while (fixture.Time.TimerCreationCount < 3)
        {
            await Task.Delay(1, fixture.CancellationToken);
        }

        var recovered = fixture.NextSnapshot();
        fixture.Time.Advance(TimeSpan.FromSeconds(2));
        await recovered.WaitAsync(fixture.CancellationToken);

        Assert.Equal(4, fixture.Factory.CreateCount);
    }

    [Fact]
    public async Task PauseSuppressesTimerAndResumeOrNetworkRefreshesImmediately()
    {
        // Given
        await using var fixture = SourceFixture.Create();
        var initial = fixture.NextSnapshot();
        await fixture.Source.StartAsync(fixture.CancellationToken);
        await initial.WaitAsync(fixture.CancellationToken);
        fixture.Availability.Pause();

        // When
        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        var whilePaused = fixture.Connection.RateLimitReadCount;
        var resumed = fixture.NextSnapshot();
        fixture.Availability.Resume();
        await resumed.WaitAsync(fixture.CancellationToken);
        var network = fixture.NextSnapshot();
        fixture.Availability.SignalNetworkAvailable();
        await network.WaitAsync(fixture.CancellationToken);

        // Then
        Assert.Equal(1, whilePaused);
        Assert.Equal(3, fixture.Connection.RateLimitReadCount);
    }

    [Fact]
    public async Task MissingAccountPublishesAuthenticationRequiredWithoutRateLimitRead()
    {
        // Given
        var connection = FakeRateLimitConnection.Valid();
        connection.AccountResult = JsonFixture.Element("""{ "account": null }""");
        await using var fixture = SourceFixture.Create(connection);

        // When
        await fixture.Source.StartAsync(fixture.CancellationToken);

        // Then
        Assert.Equal(["account/read"], connection.RequestedMethods);
        await fixture.WaitForConnectionStateAsync(QuotaConnectionState.AuthenticationRequired);
    }

    [Fact]
    public async Task ConcurrentNotificationsNeverCreateConcurrentRateLimitRequests()
    {
        // Given
        await using var fixture = SourceFixture.Create();
        await fixture.Source.StartAsync(fixture.CancellationToken);
        fixture.Connection.PauseRateLimitReads();

        // When
        for (var index = 0; index < 25; index++)
        {
            fixture.Connection.RaiseNotification(
                "account/rateLimits/updated",
                JsonFixture.Element("{}"));
        }

        await fixture.Connection.WaitForRateLimitReadCountAsync(
            2,
            fixture.CancellationToken);
        fixture.Connection.ReleaseRateLimitReads();
        await fixture.Connection.WaitForIdleAsync(fixture.CancellationToken);

        // Then
        Assert.Equal(1, fixture.Connection.MaximumConcurrentRateLimitReads);
        Assert.InRange(fixture.Connection.RateLimitReadCount, 2, 3);
    }

    [Fact]
    public async Task UnsupportedExecutablePublishesStableUnsupportedState()
    {
        // Given
        await using var fixture = SourceFixture.CreateWithExecutablePath(
            @"C:\tools\codex.ps1");

        // When
        await fixture.Source.StartAsync(fixture.CancellationToken);

        // Then
        await fixture.WaitForConnectionStateAsync(QuotaConnectionState.Unsupported);
        Assert.Equal(0, fixture.Factory.CreateCount);
    }
}
