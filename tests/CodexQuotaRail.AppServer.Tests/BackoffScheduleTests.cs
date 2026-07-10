using CodexQuotaRail.AppServer.Resilience;

namespace CodexQuotaRail.AppServer.Tests;

public sealed class BackoffScheduleTests
{
    [Fact]
    public void NextDelayFollowsCappedSequenceAndResetRestartsIt()
    {
        // Given
        var schedule = new BackoffSchedule();

        // When
        var delays = Enumerable.Range(0, 7)
            .Select(_ => schedule.NextDelay())
            .ToArray();
        schedule.Reset();
        var afterReset = schedule.NextDelay();

        // Then
        Assert.Equal(
            [
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(60),
                TimeSpan.FromSeconds(60),
                TimeSpan.FromSeconds(60),
            ],
            delays);
        Assert.Equal(TimeSpan.FromSeconds(2), afterReset);
    }
}
