using System.Text.Json;
using CodexQuotaRail.AppServer.Protocol;
using CodexQuotaRail.AppServer.Transport;

namespace CodexQuotaRail.AppServer.RateLimits;

public interface IRateLimitConnection : IAsyncDisposable
{
    event EventHandler<JsonRpcNotification>? NotificationReceived;

    Task StartAsync(CancellationToken cancellationToken);

    Task InitializeAsync(Version version, CancellationToken cancellationToken);

    Task<JsonElement> RequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken);
}

public interface IRateLimitConnectionFactory
{
    IRateLimitConnection Create(ProcessLaunchSpec launchSpec);
}

public sealed class JsonRpcRateLimitConnectionFactory(
    Action<ProcessDiagnostic>? diagnosticSink = null) : IRateLimitConnectionFactory
{
    public IRateLimitConnection Create(ProcessLaunchSpec launchSpec)
    {
        ArgumentNullException.ThrowIfNull(launchSpec);
        var transport = new ProcessJsonLineTransport(launchSpec, diagnosticSink);
        return new JsonRpcRateLimitConnection(new JsonRpcConnection(transport));
    }
}

public sealed class JsonRpcRateLimitConnection : IRateLimitConnection
{
    private readonly JsonRpcConnection _connection;

    public JsonRpcRateLimitConnection(JsonRpcConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
    }

    public event EventHandler<JsonRpcNotification>? NotificationReceived
    {
        add => _connection.NotificationReceived += value;
        remove => _connection.NotificationReceived -= value;
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        _connection.StartAsync(cancellationToken);

    public Task InitializeAsync(Version version, CancellationToken cancellationToken) =>
        _connection.InitializeAsync(version, cancellationToken);

    public Task<JsonElement> RequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken) =>
        _connection.RequestAsync(method, parameters, cancellationToken);

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
