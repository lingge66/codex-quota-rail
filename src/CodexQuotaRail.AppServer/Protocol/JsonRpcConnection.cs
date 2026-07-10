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

    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _lifecycleLock = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pendingRequests = new();
    private readonly IJsonLineTransport _transport;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private Task? _disposeTask;
    private long _nextRequestId;
    private Task? _readLoopTask;
    private ConnectionState _state;
    private Task? _startTask;
    private AppServerProtocolException? _terminalError;

    public JsonRpcConnection(IJsonLineTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
    }

    public event EventHandler<JsonRpcNotification>? NotificationReceived;

    public event EventHandler<AppServerProtocolException>? ProtocolError;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycleLock)
        {
            switch (_state)
            {
                case ConnectionState.Created:
                    _state = ConnectionState.Starting;
                    return _startTask = StartCoreAsync(cancellationToken);
                case ConnectionState.Starting:
                case ConnectionState.Started:
                case ConnectionState.Initializing:
                case ConnectionState.Initialized:
                    return _startTask!;
                case ConnectionState.Terminal:
                    throw _terminalError!;
                case ConnectionState.Disposing:
                case ConnectionState.Disposed:
                    throw new ObjectDisposedException(nameof(JsonRpcConnection));
                default:
                    throw new InvalidOperationException("未知的 App Server 连接状态。");
            }
        }
    }

    public async Task InitializeAsync(Version version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(version);
        await EnsureStartedAsync().ConfigureAwait(false);
        lock (_lifecycleLock)
        {
            ThrowIfUnavailableLocked();
            if (_state != ConnectionState.Started)
            {
                throw new InvalidOperationException("App Server 连接只能初始化一次。");
            }

            _state = ConnectionState.Initializing;
        }

        try
        {
            var versionFieldCount = version.Build >= 0 ? 3 : 2;
            await RequestCoreAsync(
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
                requireInitialized: false,
                cancellationToken).ConfigureAwait(false);

            await NotifyCoreAsync(
                "initialized",
                new { },
                requireInitialized: false,
                cancellationToken).ConfigureAwait(false);

            lock (_lifecycleLock)
            {
                ThrowIfUnavailableLocked();
                if (_state != ConnectionState.Initializing)
                {
                    throw new InvalidOperationException("App Server 初始化状态无效。");
                }

                _state = ConnectionState.Initialized;
            }
        }
        catch
        {
            SetTerminalError(
                new AppServerProtocolException("App Server 初始化失败，连接不可继续使用。"));
            throw;
        }
    }

    public Task<JsonElement> RequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken) =>
        RequestCoreAsync(method, parameters, requireInitialized: true, cancellationToken);

    public ValueTask NotifyAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken) =>
        NotifyCoreAsync(method, parameters, requireInitialized: true, cancellationToken);

    private async Task<JsonElement> RequestCoreAsync(
        string method,
        object? parameters,
        bool requireInitialized,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        await EnsureStartedAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        long id;
        TaskCompletionSource<JsonElement> completion;
        lock (_lifecycleLock)
        {
            EnsureProtocolStateLocked(requireInitialized);
            id = Interlocked.Increment(ref _nextRequestId);
            completion = new TaskCompletionSource<JsonElement>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pendingRequests.TryAdd(id, completion))
            {
                throw new InvalidOperationException("无法登记 App Server 请求。");
            }
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
                requireInitialized,
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _pendingRequests.TryRemove(id, out _);
            throw;
        }

        return await completion.Task.ConfigureAwait(false);
    }

    private async ValueTask NotifyCoreAsync(
        string method,
        object? parameters,
        bool requireInitialized,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        await EnsureStartedAsync().ConfigureAwait(false);
        lock (_lifecycleLock)
        {
            EnsureProtocolStateLocked(requireInitialized);
        }

        await WriteMessageAsync(
            new JsonRpcNotificationMessage(method, parameters),
            requireInitialized,
            cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifecycleLock)
        {
            if (_disposeTask is not null)
            {
                return new ValueTask(_disposeTask);
            }

            _state = ConnectionState.Disposing;
            _disposeTask = DisposeCoreAsync(_startTask);
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync(Task? startTask)
    {
        AppServerProtocolException? cleanupError = null;
        var disposedError = new ObjectDisposedException(nameof(JsonRpcConnection));
        FailPendingRequests(disposedError);
        try
        {
            try
            {
                _lifetimeCancellation.Cancel();
            }
            catch
            {
                cleanupError = new AppServerProtocolException("清理 App Server 连接失败。");
            }

            if (startTask is not null)
            {
                try
                {
                    await startTask.ConfigureAwait(false);
                }
                catch
                {
                }
            }

            try
            {
                await _transport.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                cleanupError ??= new AppServerProtocolException("清理 App Server 连接失败。");
            }

            Task? readLoopTask;
            lock (_lifecycleLock)
            {
                readLoopTask = _readLoopTask;
            }

            if (readLoopTask is not null)
            {
                try
                {
                    await readLoopTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
                {
                }
                catch
                {
                    cleanupError ??= new AppServerProtocolException("清理 App Server 连接失败。");
                }
            }
        }
        finally
        {
            try
            {
                _lifetimeCancellation.Dispose();
            }
            catch
            {
                cleanupError ??= new AppServerProtocolException("清理 App Server 连接失败。");
            }

            lock (_lifecycleLock)
            {
                _state = ConnectionState.Disposed;
            }
        }

        if (cleanupError is not null)
        {
            throw cleanupError;
        }
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _transport.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            var error = new AppServerProtocolException("启动 App Server 连接失败。");
            SetTerminalError(error);
            throw error;
        }

        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(
                _state is ConnectionState.Disposing or ConnectionState.Disposed,
                this);

            ThrowIfUnavailableLocked();
            if (_state != ConnectionState.Starting)
            {
                throw new InvalidOperationException("App Server 启动状态无效。");
            }

            _readLoopTask = Task.Run(
                () => ReadLoopAsync(_lifetimeCancellation.Token),
                CancellationToken.None);
            _state = ConnectionState.Started;
        }
    }

    private async Task EnsureStartedAsync()
    {
        Task? startTask;
        lock (_lifecycleLock)
        {
            ObjectDisposedException.ThrowIf(
                _state is ConnectionState.Disposing or ConnectionState.Disposed,
                this);

            startTask = _startTask;
        }

        if (startTask is null)
        {
            throw new InvalidOperationException("必须先启动 App Server 连接。");
        }

        await startTask.ConfigureAwait(false);
    }

    private async Task WriteMessageAsync(
        object message,
        bool requireInitialized,
        CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(message, SerializerOptions);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_lifecycleLock)
            {
                EnsureProtocolStateLocked(requireInitialized);
            }

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
                SetTerminalError(new AppServerProtocolException("App Server 连接已关闭。"));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                SetTerminalError(new AppServerProtocolException("读取 App Server 消息失败。"));
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
                    GenericRequestError));
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

    private void SetTerminalError(AppServerProtocolException error)
    {
        lock (_lifecycleLock)
        {
            if (_terminalError is not null ||
                _state is ConnectionState.Disposing or ConnectionState.Disposed)
            {
                return;
            }

            _terminalError = error;
            _state = ConnectionState.Terminal;
        }

        RaiseProtocolError(error);
        FailPendingRequests(error);
    }

    private void EnsureProtocolStateLocked(bool requireInitialized)
    {
        ThrowIfUnavailableLocked();
        var requiredState = requireInitialized
            ? ConnectionState.Initialized
            : ConnectionState.Initializing;
        if (_state != requiredState)
        {
            throw new InvalidOperationException("必须先完成 App Server 初始化。");
        }
    }

    private void ThrowIfUnavailableLocked()
    {
        if (_state == ConnectionState.Terminal)
        {
            throw _terminalError!;
        }

        ObjectDisposedException.ThrowIf(
            _state is ConnectionState.Disposing or ConnectionState.Disposed,
            this);
    }

    private sealed record JsonRpcRequest(string Method, long Id, object? Params);

    private sealed record JsonRpcNotificationMessage(string Method, object? Params);

    private sealed record CancellationRegistrationState(
        ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> PendingRequests,
        long Id,
        CancellationToken CancellationToken);

    private enum ConnectionState
    {
        Created,
        Starting,
        Started,
        Initializing,
        Initialized,
        Terminal,
        Disposing,
        Disposed,
    }
}
