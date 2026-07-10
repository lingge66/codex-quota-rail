using System.Text.Json;
using CodexQuotaRail.AppServer.Protocol;
using CodexQuotaRail.AppServer.RateLimits;

namespace CodexQuotaRail.AppServer.Tests;

internal sealed class FakeRateLimitConnection : IRateLimitConnection
{
    private readonly object _sync = new();
    private TaskCompletionSource _changed = NewSignal();
    private TaskCompletionSource _rateLimitGate = CompletedSignal();
    private int _activeRateLimitReads;

    private FakeRateLimitConnection()
    {
    }

    public event EventHandler<JsonRpcNotification>? NotificationReceived;

    public JsonElement AccountResult { get; set; } =
        JsonFixture.Element("""{ "account": { "type": "chatgpt" } }""");

    public bool IsDisposed { get; private set; }

    public int MaximumConcurrentRateLimitReads { get; private set; }

    public Exception? NextRateLimitException { get; set; }

    public int RateLimitReadCount { get; private set; }

    public JsonElement RateLimitsResult { get; set; } = JsonFixture.RateLimits(68);

    public List<string> RequestedMethods { get; } = [];

    public Exception? StartException { get; private init; }

    public static FakeRateLimitConnection Valid() => new();

    public static FakeRateLimitConnection StartFailure() =>
        new() { StartException = new IOException("private startup failure") };

    public Task StartAsync(CancellationToken cancellationToken) =>
        StartException is null ? Task.CompletedTask : Task.FromException(StartException);

    public Task InitializeAsync(Version version, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async Task<JsonElement> RequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            RequestedMethods.Add(method);
            if (method == "account/rateLimits/read")
            {
                RateLimitReadCount++;
                _activeRateLimitReads++;
                MaximumConcurrentRateLimitReads = Math.Max(
                    MaximumConcurrentRateLimitReads,
                    _activeRateLimitReads);
            }

            _changed.TrySetResult();
            _changed = NewSignal();
        }

        if (method == "account/read")
        {
            return AccountResult;
        }

        try
        {
            await _rateLimitGate.Task.WaitAsync(cancellationToken);
            var error = NextRateLimitException;
            NextRateLimitException = null;
            if (error is not null)
            {
                throw error;
            }

            return RateLimitsResult;
        }
        finally
        {
            lock (_sync)
            {
                _activeRateLimitReads--;
                _changed.TrySetResult();
                _changed = NewSignal();
            }
        }
    }

    public void RaiseNotification(string method, JsonElement parameters) =>
        NotificationReceived?.Invoke(this, new JsonRpcNotification(method, parameters));

    public void PauseRateLimitReads() => _rateLimitGate = NewSignal();

    public void ReleaseRateLimitReads() => _rateLimitGate.TrySetResult();

    public Task WaitForRateLimitReadCountAsync(
        int expected,
        CancellationToken cancellationToken) =>
        WaitForConditionAsync(() => RateLimitReadCount >= expected, cancellationToken);

    public Task WaitForIdleAsync(CancellationToken cancellationToken) =>
        WaitForConditionAsync(() => _activeRateLimitReads == 0, cancellationToken);

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        ReleaseRateLimitReads();
        return ValueTask.CompletedTask;
    }

    private async Task WaitForConditionAsync(
        Func<bool> condition,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            Task changed;
            lock (_sync)
            {
                if (condition())
                {
                    return;
                }

                changed = _changed.Task;
            }

            await changed.WaitAsync(cancellationToken);
        }
    }

    private static TaskCompletionSource CompletedSignal()
    {
        var signal = NewSignal();
        signal.TrySetResult();
        return signal;
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
