using CodexQuotaRail.App.Hosting;

namespace CodexQuotaRail.App.Tests;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public async Task SecondaryInstanceSignalsPrimaryThroughCurrentUserPipe()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var activated = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var primary = SingleInstanceGuard.Acquire(
            $"Local\\CodexQuotaRail.Tests.{suffix}",
            $"CodexQuotaRail.Tests.{suffix}.Activate",
            () => activated.TrySetResult());
        await using var secondary = SingleInstanceGuard.Acquire(
            $"Local\\CodexQuotaRail.Tests.{suffix}",
            $"CodexQuotaRail.Tests.{suffix}.Activate",
            () => { });

        Assert.True(primary.IsPrimary);
        Assert.False(secondary.IsPrimary);
        Assert.True(await secondary.SignalPrimaryAsync(TimeSpan.FromSeconds(2)));
        await activated.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
