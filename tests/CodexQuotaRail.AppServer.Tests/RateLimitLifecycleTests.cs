using CodexQuotaRail.Core.Quotas;

namespace CodexQuotaRail.AppServer.Tests;

public sealed class RateLimitLifecycleTests
{
    [Fact]
    public async Task PauseWinsWhenDueTimerCallbackIsAlreadyQueued()
    {
        // Given
        await using var fixture = SourceFixture.Create();
        await fixture.Source.StartAsync(fixture.CancellationToken);
        var callbackEntered = NewSignal();
        var releaseCallback = NewSignal();
        fixture.Time.BeforeTimerCallback = () =>
        {
            callbackEntered.TrySetResult();
            releaseCallback.Task.GetAwaiter().GetResult();
        };

        // When
        var advance = Task.Run(
            () => fixture.Time.Advance(TimeSpan.FromSeconds(60)),
            fixture.CancellationToken);
        await callbackEntered.Task.WaitAsync(fixture.CancellationToken);
        fixture.Availability.Pause();
        releaseCallback.TrySetResult();
        await advance;

        // Then
        Assert.Equal(1, fixture.Connection.RateLimitReadCount);
        var resumed = fixture.NextSnapshot();
        fixture.Availability.Resume();
        await resumed.WaitAsync(fixture.CancellationToken);
        Assert.Equal(2, fixture.Connection.RateLimitReadCount);
    }

    [Fact]
    public async Task DisposeWinsWhenDueTimerCallbackIsAlreadyQueued()
    {
        // Given
        var fixture = SourceFixture.Create();
        await fixture.Source.StartAsync(fixture.CancellationToken);
        var callbackEntered = NewSignal();
        var releaseCallback = NewSignal();
        fixture.Time.BeforeTimerCallback = () =>
        {
            callbackEntered.TrySetResult();
            releaseCallback.Task.GetAwaiter().GetResult();
        };

        // When
        var advance = Task.Run(
            () => fixture.Time.Advance(TimeSpan.FromSeconds(60)),
            fixture.CancellationToken);
        await callbackEntered.Task.WaitAsync(fixture.CancellationToken);
        var dispose = fixture.Source.DisposeAsync().AsTask();
        releaseCallback.TrySetResult();
        await Task.WhenAll(advance, dispose).WaitAsync(fixture.CancellationToken);

        // Then
        Assert.True(fixture.Connection.IsDisposed);
        Assert.Equal(1, fixture.Connection.RateLimitReadCount);
        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task DisposeFailsInFlightRefreshWaiterAndReleasesConnection()
    {
        // Given
        var fixture = SourceFixture.Create();
        await fixture.Source.StartAsync(fixture.CancellationToken);
        fixture.Connection.PauseRateLimitReads();
        var refresh = fixture.Source.RefreshAsync(fixture.CancellationToken);
        await fixture.Connection.WaitForRateLimitReadCountAsync(
            2,
            fixture.CancellationToken);

        // When
        var dispose = fixture.Source.DisposeAsync().AsTask();
        var error = await Record.ExceptionAsync(
            () => refresh.WaitAsync(fixture.CancellationToken));
        await dispose.WaitAsync(fixture.CancellationToken);

        // Then
        Assert.IsType<ObjectDisposedException>(error);
        Assert.True(fixture.Connection.IsDisposed);
        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task StartCancellationRemovesOnlyCallerWaiterAndSourceRecovers()
    {
        // Given
        await using var fixture = SourceFixture.CreatePaused();
        using var callerCancellation = new CancellationTokenSource();
        var start = fixture.Source.StartAsync(callerCancellation.Token);
        Assert.Equal(1, fixture.Source.PendingWaiterCount);

        // When
        callerCancellation.Cancel();
        var error = await Record.ExceptionAsync(() => start);

        // Then
        Assert.IsType<TaskCanceledException>(error);
        Assert.Equal(0, fixture.Source.PendingWaiterCount);
        var snapshot = fixture.NextSnapshot();
        fixture.Availability.Resume();
        await snapshot.WaitAsync(fixture.CancellationToken);
        Assert.Equal(1, fixture.Connection.RateLimitReadCount);
    }

    [Fact]
    public async Task CanceledRefreshWaitersDoNotAccumulateDuringLongPause()
    {
        // Given
        await using var fixture = SourceFixture.Create();
        await fixture.Source.StartAsync(fixture.CancellationToken);
        fixture.Availability.Pause();
        var cancellations = Enumerable.Range(0, 32)
            .Select(_ => new CancellationTokenSource())
            .ToArray();
        var refreshes = cancellations
            .Select(cancellation => fixture.Source.RefreshAsync(cancellation.Token))
            .ToArray();
        Assert.Equal(32, fixture.Source.PendingWaiterCount);

        // When
        foreach (var cancellation in cancellations)
        {
            cancellation.Cancel();
        }

        await Task.WhenAll(
            refreshes.Select(
                async refresh => Assert.IsType<TaskCanceledException>(
                    await Record.ExceptionAsync(() => refresh))));

        // Then
        Assert.Equal(0, fixture.Source.PendingWaiterCount);
        var snapshot = fixture.NextSnapshot();
        fixture.Availability.Resume();
        await snapshot.WaitAsync(fixture.CancellationToken);
        Assert.Equal(2, fixture.Connection.RateLimitReadCount);
        foreach (var cancellation in cancellations)
        {
            cancellation.Dispose();
        }
    }

    [Fact]
    public async Task DisposeCannotPassQueuedRefreshBeforeSignalRelease()
    {
        // Given
        var fixture = SourceFixture.Create();
        var releaseSignal = NewSignal();
        Task? dispose = null;
        try
        {
            await fixture.Source.StartAsync(fixture.CancellationToken);
            var queued = NewSignal();
            fixture.Source.BeforeRefreshSignalRelease = () =>
            {
                queued.TrySetResult();
                releaseSignal.Task.GetAwaiter().GetResult();
            };
            var refresh = Task.Run(
                () => fixture.Source.RefreshAsync(fixture.CancellationToken),
                fixture.CancellationToken);
            await queued.Task.WaitAsync(fixture.CancellationToken);

            // When
            dispose = Task.Run(
                async () => await fixture.Source.DisposeAsync(),
                fixture.CancellationToken);
            var disposePassedBarrier = await CompletesSoonAsync(dispose);
            releaseSignal.TrySetResult();
            var refreshError = await Record.ExceptionAsync(
                () => refresh.WaitAsync(fixture.CancellationToken));
            await dispose.WaitAsync(fixture.CancellationToken);

            // Then
            Assert.False(disposePassedBarrier);
            Assert.True(
                refreshError is null or ObjectDisposedException,
                "已安全 Release 的刷新只能成功或由 Dispose 终止。");
            Assert.Equal(0, fixture.Source.PendingWaiterCount);
            Assert.True(fixture.Connection.IsDisposed);
        }
        finally
        {
            releaseSignal.TrySetResult();
            if (dispose is not null)
            {
                await Record.ExceptionAsync(() => dispose);
            }

            await fixture.DisposeAsync();
        }
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<bool> CompletesSoonAsync(Task task) =>
        ReferenceEquals(
            task,
            await Task.WhenAny(task, Task.Delay(TimeSpan.FromMilliseconds(250))));
}
