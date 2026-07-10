namespace CodexQuotaRail.AppServer.Tests;

public sealed class RateLimitCallbackTests
{
    [Fact]
    public async Task SnapshotCallbackCanSynchronouslyWaitForRefresh()
    {
        // Given
        await using var fixture = SourceFixture.Create();
        using var callbackTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var callbackOutcome = new TaskCompletionSource<Exception?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var invoked = 0;
        fixture.Source.SnapshotChanged += (_, _) =>
        {
            if (Interlocked.Exchange(ref invoked, 1) != 0)
            {
                return;
            }

            try
            {
                fixture.Source.RefreshAsync(callbackTimeout.Token).GetAwaiter().GetResult();
                callbackOutcome.TrySetResult(null);
            }
            catch (Exception error)
            {
                callbackOutcome.TrySetResult(error);
            }
        };

        // When
        await fixture.Source.StartAsync(fixture.CancellationToken);
        var outcome = await callbackOutcome.Task.WaitAsync(fixture.CancellationToken);

        // Then
        Assert.Null(outcome);
        Assert.Equal(2, fixture.Connection.RateLimitReadCount);
    }

    [Fact]
    public async Task SnapshotCallbackCanSynchronouslyWaitForDispose()
    {
        // Given
        var fixture = SourceFixture.Create();
        var callbackOutcome = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Source.SnapshotChanged += (_, _) =>
        {
            var completed = fixture.Source.DisposeAsync()
                .AsTask()
                .Wait(TimeSpan.FromSeconds(2));
            callbackOutcome.TrySetResult(completed);
        };

        // When
        var start = fixture.Source.StartAsync(fixture.CancellationToken);
        var disposeCompleted = await callbackOutcome.Task.WaitAsync(fixture.CancellationToken);
        await Record.ExceptionAsync(() => start.WaitAsync(fixture.CancellationToken));

        // Then
        Assert.True(disposeCompleted);
        Assert.True(fixture.Connection.IsDisposed);
        await fixture.DisposeAsync();
    }

    [Fact]
    public async Task DisposeClearsQueuedCallbacksAndReportsDrops()
    {
        // Given
        var fixture = SourceFixture.Create();
        var callbackEntered = NewSignal();
        var releaseCallback = NewSignal();
        var callbackCount = 0;
        fixture.Source.SnapshotChanged += (_, _) =>
        {
            Interlocked.Increment(ref callbackCount);
            callbackEntered.TrySetResult();
            releaseCallback.Task.GetAwaiter().GetResult();
        };
        var start = fixture.Source.StartAsync(fixture.CancellationToken);
        await callbackEntered.Task.WaitAsync(fixture.CancellationToken);

        // When
        var dispose = fixture.Source.DisposeAsync().AsTask();
        releaseCallback.TrySetResult();
        await dispose.WaitAsync(fixture.CancellationToken);
        await Record.ExceptionAsync(() => start.WaitAsync(fixture.CancellationToken));

        // Then
        Assert.Equal(1, Volatile.Read(ref callbackCount));
        Assert.True(fixture.Source.DroppedCallbackCount > 0);
        await fixture.DisposeAsync();
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
