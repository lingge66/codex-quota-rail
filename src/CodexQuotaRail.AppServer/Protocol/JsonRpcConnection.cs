using System.Collections.Concurrent;
using System.Text.Json;
using CodexQuotaRail.AppServer.Transport;

namespace CodexQuotaRail.AppServer.Protocol;

public sealed class JsonRpcConnection : IAsyncDisposable
{
    private const string GenericProtocolError = "收到无效的 App Server 协议消息。";
    private const string GenericRequestError = "App Server 请求失败。";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly string[] SensitiveMarkers =
    [
        "api_key",
        "apikey",
        "authorization",
        "bearer",
        "credential",
        "password",
        "secret",
        "session",
        "sk-",
        "token",
    ];

    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pendingRequests = new();
    private readonly object _startLock = new();
    private readonly IJsonLineTransport _transport;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private int _disposeState;
    private long _nextRequestId;
    private Task? _readLoopTask;
    private Task? _startTask;

    public JsonRpcConnection(IJsonLineTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
    }

    public event EventHandler<JsonRpcNotification>? NotificationReceived;

    public event EventHandler<AppServerProtocolException>? ProtocolError;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        lock (_startLock)
        {
            ThrowIfDisposed();
            return _startTask ??= StartCoreAsync(cancellationToken);
        }
    }

    public async Task InitializeAsync(Version version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(version);

        var versionFieldCount = version.Build >= 0 ? 3 : 2;
        await RequestAsync(
            "initialize",
            new
            {
                clientInfo = new
                {
                    name = "codex_quota_rail",
                    title = "Codex Quota Rail",
                    version = version.ToString(versionFieldCount),
                },
            },
            cancellationToken).ConfigureAwait(false);

        await NotifyAsync("initialized", new { }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JsonElement> RequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        await EnsureStartedAsync().ConfigureAwait(false);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        var id = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingRequests.TryAdd(id, completion))
        {
            throw new InvalidOperationException("无法登记 App Server 请求。");
        }

        using var cancellationRegistration = cancellationToken.Register(
            static state =>
            {
                var registration = (CancellationRegistrationState)state!;
                if (registration.PendingRequests.TryRemove(registration.Id, out var pending))
                {
                    pending.TrySetCanceled(registration.CancellationToken);
                }
            },
            new CancellationRegistrationState(
                _pendingRequests,
                id,
                cancellationToken));

        try
        {
            await WriteMessageAsync(
                new JsonRpcRequest(method, id, parameters),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _pendingRequests.TryRemove(id, out _);
            throw;
        }

        return await completion.Task.ConfigureAwait(false);
    }

    public async ValueTask NotifyAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        await EnsureStartedAsync().ConfigureAwait(false);
        ThrowIfDisposed();
        await WriteMessageAsync(
            new JsonRpcNotificationMessage(method, parameters),
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        var disposedError = new ObjectDisposedException(nameof(JsonRpcConnection));
        FailPendingRequests(disposedError);
        _lifetimeCancellation.Cancel();

        await _transport.DisposeAsync().ConfigureAwait(false);

        var readLoopTask = _readLoopTask;
        if (readLoopTask is not null)
        {
            try
            {
                await readLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
            }
        }

        _lifetimeCancellation.Dispose();
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        await _transport.StartAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfDisposed();
        _readLoopTask = Task.Run(
            () => ReadLoopAsync(_lifetimeCancellation.Token),
            CancellationToken.None);
    }

    private async Task EnsureStartedAsync()
    {
        Task? startTask;
        lock (_startLock)
        {
            startTask = _startTask;
        }

        if (startTask is null)
        {
            throw new InvalidOperationException("必须先启动 App Server 连接。");
        }

        await startTask.ConfigureAwait(false);
    }

    private async Task WriteMessageAsync(object message, CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(message, SerializerOptions);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            await _transport.WriteLineAsync(line, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var line in _transport.ReadLinesAsync(cancellationToken).ConfigureAwait(false))
            {
                HandleLine(line);
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                var error = new AppServerProtocolException("App Server 连接已关闭。");
                RaiseProtocolError(error);
                FailPendingRequests(error);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                var error = new AppServerProtocolException("读取 App Server 消息失败。");
                RaiseProtocolError(error);
                FailPendingRequests(error);
            }
        }
    }

    private void HandleLine(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new AppServerProtocolException(GenericProtocolError);
            }

            if (root.TryGetProperty("id", out var idElement))
            {
                HandleResponse(root, idElement);
                return;
            }

            if (root.TryGetProperty("method", out var methodElement) &&
                methodElement.ValueKind == JsonValueKind.String)
            {
                HandleNotification(root, methodElement.GetString()!);
                return;
            }

            throw new AppServerProtocolException(GenericProtocolError);
        }
        catch (JsonException)
        {
            RaiseProtocolError(new AppServerProtocolException(GenericProtocolError));
        }
        catch (AppServerProtocolException error)
        {
            RaiseProtocolError(error);
        }
    }

    private void HandleResponse(JsonElement root, JsonElement idElement)
    {
        if (idElement.ValueKind != JsonValueKind.Number || !idElement.TryGetInt64(out var id))
        {
            throw new AppServerProtocolException(GenericProtocolError);
        }

        if (!_pendingRequests.TryRemove(id, out var completion))
        {
            return;
        }

        if (root.TryGetProperty("error", out var errorElement))
        {
            if (!TryReadServerError(errorElement, out var serverError))
            {
                var protocolError = new AppServerProtocolException(GenericProtocolError);
                completion.TrySetException(protocolError);
                RaiseProtocolError(protocolError);
                return;
            }

            completion.TrySetException(
                new AppServerRequestException(
                    serverError.Code,
                    SanitizeServerMessage(serverError.Message)));
            return;
        }

        if (root.TryGetProperty("result", out var resultElement))
        {
            completion.TrySetResult(resultElement.Clone());
            return;
        }

        var missingResultError = new AppServerProtocolException(GenericProtocolError);
        completion.TrySetException(missingResultError);
        RaiseProtocolError(missingResultError);
    }

    private void HandleNotification(JsonElement root, string method)
    {
        var parameters = root.TryGetProperty("params", out var paramsElement)
            ? paramsElement.Clone()
            : JsonSerializer.SerializeToElement(new { });
        RaiseNotification(new JsonRpcNotification(method, parameters));
    }

    private static bool TryReadServerError(
        JsonElement errorElement,
        out JsonRpcServerError serverError)
    {
        if (errorElement.ValueKind == JsonValueKind.Object &&
            errorElement.TryGetProperty("code", out var codeElement) &&
            codeElement.TryGetInt32(out var code) &&
            errorElement.TryGetProperty("message", out var messageElement) &&
            messageElement.ValueKind == JsonValueKind.String)
        {
            serverError = new JsonRpcServerError(code, messageElement.GetString()!);
            return true;
        }

        serverError = new JsonRpcServerError(0, GenericRequestError);
        return false;
    }

    private static string SanitizeServerMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 256)
        {
            return GenericRequestError;
        }

        if (message.Any(character => char.IsControl(character) && !char.IsWhiteSpace(character)))
        {
            return GenericRequestError;
        }

        foreach (var marker in SensitiveMarkers)
        {
            if (message.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return GenericRequestError;
            }
        }

        return message;
    }

    private void RaiseNotification(JsonRpcNotification notification)
    {
        var handlers = NotificationReceived;
        if (handlers is null)
        {
            return;
        }

        foreach (var subscriber in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<JsonRpcNotification>)subscriber)(this, notification);
            }
            catch
            {
            }
        }
    }

    private void RaiseProtocolError(AppServerProtocolException error)
    {
        var handlers = ProtocolError;
        if (handlers is null)
        {
            return;
        }

        foreach (var subscriber in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<AppServerProtocolException>)subscriber)(this, error);
            }
            catch
            {
            }
        }
    }

    private void FailPendingRequests(Exception error)
    {
        foreach (var request in _pendingRequests)
        {
            if (_pendingRequests.TryRemove(request.Key, out var completion))
            {
                completion.TrySetException(error);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposeState) != 0,
            this);
    }

    private sealed record JsonRpcRequest(string Method, long Id, object? Params);

    private sealed record JsonRpcNotificationMessage(string Method, object? Params);

    private sealed record CancellationRegistrationState(
        ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> PendingRequests,
        long Id,
        CancellationToken CancellationToken);
}
