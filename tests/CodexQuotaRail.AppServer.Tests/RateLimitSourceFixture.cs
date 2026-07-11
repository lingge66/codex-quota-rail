using System.Collections.Concurrent;
using CodexQuotaRail.AppServer.Discovery;
using CodexQuotaRail.AppServer.RateLimits;
using CodexQuotaRail.AppServer.Transport;
using CodexQuotaRail.Core.Quotas;

namespace CodexQuotaRail.AppServer.Tests;

internal sealed class SourceFixture : IAsyncDisposable
{
    private readonly ConcurrentQueue<TaskCompletionSource<RawQuotaSnapshot>> _snapshotWaiters = new();
    private readonly ConcurrentQueue<TaskCompletionSource<QuotaConnectionState>> _connectionWaiters = new();
    private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(10));

    private SourceFixture(
        RateLimitSource source,
        FakeRateLimitConnectionFactory factory,
        FakeRateLimitAvailabilitySignal availability,
        ManualTimeProvider time,
        FakeRateLimitConnection connection)
    {
        Source = source;
        Factory = factory;
        Availability = availability;
        Time = time;
        Connection = connection;
        Source.ConnectionChanged += (_, state) =>
        {
            ConnectionStates.Add(state);
            if (_connectionWaiters.TryDequeue(out var waiter))
            {
                waiter.TrySetResult(state);
            }
        };
        Source.SnapshotChanged += (_, snapshot) =>
        {
            if (_snapshotWaiters.TryDequeue(out var waiter))
            {
                waiter.TrySetResult(snapshot);
            }
        };
    }

    public FakeRateLimitAvailabilitySignal Availability { get; }

    public CancellationToken CancellationToken => _timeout.Token;

    public FakeRateLimitConnection Connection { get; }

    public List<QuotaConnectionState> ConnectionStates { get; } = [];

    public FakeRateLimitConnectionFactory Factory { get; }

    public RateLimitSource Source { get; }

    public ManualTimeProvider Time { get; }

    public static SourceFixture Create(params FakeRateLimitConnection[] connections)
        => CreateCore(@"C:\tools\codex.exe", initiallyPaused: false, connections);

    public static SourceFixture CreatePaused(params FakeRateLimitConnection[] connections)
        => CreateCore(@"C:\tools\codex.exe", initiallyPaused: true, connections);

    public static SourceFixture CreateWithExecutablePath(
        string executablePath,
        params FakeRateLimitConnection[] connections)
        => CreateCore(executablePath, initiallyPaused: false, connections);

    private static SourceFixture CreateCore(
        string executablePath,
        bool initiallyPaused,
        params FakeRateLimitConnection[] connections)
    {
        var selected = connections.Length == 0
            ? [FakeRateLimitConnection.Valid()]
            : connections;
        var factory = new FakeRateLimitConnectionFactory(selected);
        var availability = new FakeRateLimitAvailabilitySignal();
        if (initiallyPaused)
        {
            availability.Pause();
        }

        var time = new ManualTimeProvider();
        var source = new RateLimitSource(
            new RateLimitSourceDependencies(
                new CodexExecutableResolver(
                    new FakeSourceDiscoveryProbe(executablePath)),
                factory,
                availability,
                time,
                new Version(1, 0, 0)));
        return new SourceFixture(source, factory, availability, time, selected[0]);
    }

    public Task<RawQuotaSnapshot> NextSnapshot()
    {
        var waiter = new TaskCompletionSource<RawQuotaSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _snapshotWaiters.Enqueue(waiter);
        return waiter.Task;
    }

    public Task<QuotaConnectionState> NextConnectionState()
    {
        var waiter = new TaskCompletionSource<QuotaConnectionState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _connectionWaiters.Enqueue(waiter);
        return waiter.Task;
    }

    public Task WaitForFactoryCountAsync(int expected) =>
        Factory.WaitForCreateCountAsync(expected, CancellationToken);

    public async ValueTask DisposeAsync()
    {
        await Source.DisposeAsync();
        _timeout.Dispose();
    }
}

internal sealed class FakeRateLimitConnectionFactory(
    IReadOnlyList<FakeRateLimitConnection> connections) : IRateLimitConnectionFactory
{
    private readonly Queue<FakeRateLimitConnection> _connections = new(connections);
    private readonly object _sync = new();
    private TaskCompletionSource _changed = NewSignal();

    public int CreateCount { get; private set; }

    public List<ProcessLaunchSpec> LaunchSpecs { get; } = [];

    public IRateLimitConnection Create(ProcessLaunchSpec launchSpec)
    {
        lock (_sync)
        {
            LaunchSpecs.Add(launchSpec);
            CreateCount++;
            var connection = _connections.Dequeue();
            _changed.TrySetResult();
            _changed = NewSignal();
            return connection;
        }
    }

    public async Task WaitForCreateCountAsync(int expected, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task changed;
            lock (_sync)
            {
                if (CreateCount >= expected)
                {
                    return;
                }

                changed = _changed.Task;
            }

            await changed.WaitAsync(cancellationToken);
        }
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
