using System.Diagnostics;
using CodexQuotaRail.AppServer.Discovery;

namespace CodexQuotaRail.AppServer.Tests;

public sealed class BoundedProcessRunnerTests
{
    private static readonly TimeSpan MainTimeout = TimeSpan.FromSeconds(3);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RunDrainsFloodedStreamButRetainsOnlyFixedLimit(bool floodStandardOutput)
    {
        // Given
        var flood = new string('x', BoundedProcessRunner.OutputCharacterLimit * 4);
        var process = FakeBoundedProcess.Exited(
            floodStandardOutput ? flood : string.Empty,
            floodStandardOutput ? string.Empty : flood);
        var runner = new BoundedProcessRunner(new SingleProcessFactory(process));

        // When
        var result = runner.Run(NewStartInfo(), MainTimeout);

        // Then
        Assert.True(result.Succeeded);
        Assert.Equal(
            BoundedProcessRunner.OutputCharacterLimit,
            floodStandardOutput
                ? result.StandardOutput.Length
                : result.StandardError.Length);
        Assert.Equal(floodStandardOutput, result.StandardOutputTruncated);
        Assert.Equal(!floodStandardOutput, result.StandardErrorTruncated);
        Assert.True(process.IsDisposed);
    }

    [Fact]
    public async Task RunKillsProcessAtMainDeadlineAndObservesDrains()
    {
        // Given
        var time = new ManualTimeProvider();
        var process = FakeBoundedProcess.Running(completeWhenKilled: true);
        var runner = new BoundedProcessRunner(new SingleProcessFactory(process), time);
        var run = Task.Run(() => runner.Run(NewStartInfo(), MainTimeout));
        await process.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // When
        time.Advance(MainTimeout);
        var result = await run.WaitAsync(TimeSpan.FromSeconds(2));

        // Then
        Assert.True(result.TimedOut);
        Assert.Equal(1, process.KillCount);
        Assert.True(process.IsDisposed);
        Assert.True(process.OutputReadCompleted);
        Assert.True(process.ErrorReadCompleted);
    }

    [Fact]
    public async Task RunStopsAfterKillGraceWhenProcessNeverReportsExit()
    {
        // Given
        var time = new ManualTimeProvider();
        var process = FakeBoundedProcess.Running(completeWhenKilled: false);
        var runner = new BoundedProcessRunner(new SingleProcessFactory(process), time);
        var run = Task.Run(() => runner.Run(NewStartInfo(), MainTimeout));
        await process.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        time.Advance(MainTimeout);
        await process.Killed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // When
        time.Advance(BoundedProcessRunner.KillGrace);
        var result = await run.WaitAsync(TimeSpan.FromSeconds(2));

        // Then
        Assert.True(result.TimedOut);
        Assert.False(result.Exited);
        Assert.Equal(1, process.KillCount);
        Assert.True(process.IsDisposed);
    }

    [Fact]
    public async Task RunBoundsSynchronousStartWithinMainDeadlineAndKillGrace()
    {
        // Given
        var time = new ManualTimeProvider();
        var process = FakeBoundedProcess.Starting();
        var runner = new BoundedProcessRunner(new SingleProcessFactory(process), time);
        var run = Task.Run(() => runner.Run(NewStartInfo(), MainTimeout));
        await process.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // When
        time.Advance(MainTimeout);
        var killObserved = await CompletesSoonAsync(process.Killed.Task);
        if (killObserved)
        {
            time.Advance(BoundedProcessRunner.KillGrace);
        }

        var runCompleted = await CompletesSoonAsync(run);
        process.ReleaseStart();
        await process.StartCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        if (!killObserved)
        {
            await process.Killed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            time.Advance(BoundedProcessRunner.KillGrace);
        }

        var result = await run.WaitAsync(TimeSpan.FromSeconds(2));

        // Then
        Assert.True(killObserved);
        Assert.True(runCompleted);
        Assert.True(result.TimedOut);
        Assert.True(process.IsDisposed);
    }

    [Fact]
    public async Task RunBoundsBlockingKillWithinSingleKillGrace()
    {
        // Given
        var time = new ManualTimeProvider();
        var process = FakeBoundedProcess.Killing();
        var runner = new BoundedProcessRunner(new SingleProcessFactory(process), time);
        var run = Task.Run(() => runner.Run(NewStartInfo(), MainTimeout));
        await process.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var runCompleted = false;
        var advanceMainDeadline = Task.Run(() => time.Advance(MainTimeout));
        try
        {
            await process.Killed.Task.WaitAsync(TimeSpan.FromSeconds(2));

            // When
            time.Advance(BoundedProcessRunner.KillGrace);
            runCompleted = await CompletesSoonAsync(run);
        }
        finally
        {
            process.ReleaseKill();
            time.Advance(BoundedProcessRunner.KillGrace);
        }

        await advanceMainDeadline.WaitAsync(TimeSpan.FromSeconds(2));
        await process.KillCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var result = await run.WaitAsync(TimeSpan.FromSeconds(2));

        // Then
        Assert.True(runCompleted);
        Assert.True(result.TimedOut);
        Assert.True(process.IsDisposed);
    }

    private static ProcessStartInfo NewStartInfo() => new()
    {
        FileName = "bounded-process-test",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
    };

    private static async Task<bool> CompletesSoonAsync(Task task) =>
        ReferenceEquals(
            task,
            await Task.WhenAny(task, Task.Delay(TimeSpan.FromMilliseconds(250))));

}
