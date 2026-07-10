using System.Collections.Concurrent;
using System.Text.Json;
using CodexQuotaRail.AppServer.Protocol;
using CodexQuotaRail.AppServer.Transport;

namespace CodexQuotaRail.AppServer.Tests;

public sealed class JsonRpcConnectionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task InitializeUsesClientIdentityAndNotifiesOnlyAfterSuccess()
    {
        var transport = new FakeJsonLineTransport();
        await using var connection = new JsonRpcConnection(transport);
        await connection.StartAsync(CancellationToken.None);

        var initializeTask = connection.InitializeAsync(new Version(0, 1, 0), CancellationToken.None);
        var initializeLine = await ReadWrittenLineAsync(transport);

        using (var document = JsonDocument.Parse(initializeLine))
        {
            var root = document.RootElement;
            Assert.False(root.TryGetProperty("jsonrpc", out _));
            Assert.Equal("initialize", root.GetProperty("method").GetString());
            Assert.Equal(1, root.GetProperty("id").GetInt64());
            Assert.False(root.GetProperty("params").TryGetProperty("experimentalApi", out _));

            var clientInfo = root.GetProperty("params").GetProperty("clientInfo");
            Assert.Equal("codex_quota_rail", clientInfo.GetProperty("name").GetString());
            Assert.Equal("Codex Quota Rail", clientInfo.GetProperty("title").GetString());
            Assert.Equal("0.1.0", clientInfo.GetProperty("version").GetString());
        }

        Assert.False(transport.TryReadWrittenLine(out _));

        transport.QueueIncoming("{\"id\":1,\"result\":{\"userAgent\":\"test\"}}");
        await initializeTask.WaitAsync(TestTimeout);

        var initializedLine = await ReadWrittenLineAsync(transport);
        using var initializedDocument = JsonDocument.Parse(initializedLine);
        var initialized = initializedDocument.RootElement;
        Assert.False(initialized.TryGetProperty("jsonrpc", out _));
        Assert.Equal("initialized", initialized.GetProperty("method").GetString());
        Assert.Equal(JsonValueKind.Object, initialized.GetProperty("params").ValueKind);
        Assert.Empty(initialized.GetProperty("params").EnumerateObject());
    }

    [Fact]
    public async Task InitializeFailureDoesNotSendInitializedNotification()
    {
        var transport = new FakeJsonLineTransport();
        await using var connection = new JsonRpcConnection(transport);
        await connection.StartAsync(CancellationToken.None);

        var initializeTask = connection.InitializeAsync(new Version(0, 1, 0), CancellationToken.None);
        _ = await ReadWrittenLineAsync(transport);
        transport.QueueIncoming("{\"id\":1,\"error\":{\"code\":-32000,\"message\":\"初始化失败\"}}");

        var error = await Assert.ThrowsAsync<AppServerRequestException>(
            () => initializeTask.WaitAsync(TestTimeout));
        Assert.Equal(-32000, error.Code);
        Assert.Equal("初始化失败", error.Message);
        Assert.False(transport.TryReadWrittenLine(out _));
    }

    [Fact]
    public async Task ConcurrentRequestsAreCorrelatedWhenResponsesArriveOutOfOrder()
    {
        var transport = new FakeJsonLineTransport();
        await using var connection = new JsonRpcConnection(transport);
        await connection.StartAsync(CancellationToken.None);

        var firstTask = connection.RequestAsync("first/read", new { marker = "first" }, CancellationToken.None);
        var secondTask = connection.RequestAsync("second/read", new { marker = "second" }, CancellationToken.None);
        var requests = new[]
        {
            ParseRequest(await ReadWrittenLineAsync(transport)),
            ParseRequest(await ReadWrittenLineAsync(transport)),
        }.ToDictionary(request => request.Method);

        transport.QueueIncoming(
            $"{{\"id\":{requests["second/read"].Id},\"result\":{{\"value\":\"second\"}}}}");

        var second = await secondTask.WaitAsync(TestTimeout);
        Assert.Equal("second", second.GetProperty("value").GetString());
        Assert.False(firstTask.IsCompleted);

        transport.QueueIncoming(
            $"{{\"id\":{requests["first/read"].Id},\"result\":{{\"value\":\"first\"}}}}");

        var first = await firstTask.WaitAsync(TestTimeout);
        Assert.Equal("first", first.GetProperty("value").GetString());
    }

    [Fact]
    public async Task CancellationRemovesPendingRequestAndLateResponseIsIgnored()
    {
        var transport = new FakeJsonLineTransport();
        await using var connection = new JsonRpcConnection(transport);
        await connection.StartAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();

        var cancelledTask = connection.RequestAsync("slow/read", null, cancellation.Token);
        var cancelledRequest = ParseRequest(await ReadWrittenLineAsync(transport));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cancelledTask.WaitAsync(TestTimeout));
        transport.QueueIncoming(
            $"{{\"id\":{cancelledRequest.Id},\"result\":{{\"ignored\":true}}}}");

        var liveTask = connection.RequestAsync("live/read", null, CancellationToken.None);
        var liveRequest = ParseRequest(await ReadWrittenLineAsync(transport));
        transport.QueueIncoming(
            $"{{\"id\":{liveRequest.Id},\"result\":{{\"alive\":true}}}}");

        var liveResult = await liveTask.WaitAsync(TestTimeout);
        Assert.True(liveResult.GetProperty("alive").GetBoolean());
    }

    [Fact]
    public async Task MalformedJsonReportsSanitizedProtocolErrorAndReadLoopContinues()
    {
        const string Secret = "sk-should-never-be-reported";
        var transport = new FakeJsonLineTransport();
        await using var connection = new JsonRpcConnection(transport);
        var protocolError = NewCompletionSource<AppServerProtocolException>();
        connection.ProtocolError += (_, error) => protocolError.TrySetResult(error);
        await connection.StartAsync(CancellationToken.None);

        transport.QueueIncoming($"{{\"token\":\"{Secret}\"");

        var error = await protocolError.Task.WaitAsync(TestTimeout);
        Assert.DoesNotContain(Secret, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("token", error.Message, StringComparison.OrdinalIgnoreCase);

        var requestTask = connection.RequestAsync("after-error/read", null, CancellationToken.None);
        var request = ParseRequest(await ReadWrittenLineAsync(transport));
        transport.QueueIncoming(
            $"{{\"id\":{request.Id},\"result\":{{\"ok\":true}}}}");
        Assert.True((await requestTask.WaitAsync(TestTimeout)).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task StructurallyInvalidJsonDoesNotExposeItsRawSecret()
    {
        const string Secret = "Bearer private-credential";
        var transport = new FakeJsonLineTransport();
        await using var connection = new JsonRpcConnection(transport);
        var protocolError = NewCompletionSource<AppServerProtocolException>();
        connection.ProtocolError += (_, error) => protocolError.TrySetResult(error);
        await connection.StartAsync(CancellationToken.None);

        transport.QueueIncoming($"[\"{Secret}\"]");

        var error = await protocolError.Task.WaitAsync(TestTimeout);
        Assert.DoesNotContain(Secret, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private-credential", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerErrorPreservesCodeAndSafeMessage()
    {
        var transport = new FakeJsonLineTransport();
        await using var connection = new JsonRpcConnection(transport);
        await connection.StartAsync(CancellationToken.None);

        var requestTask = connection.RequestAsync("account/read", null, CancellationToken.None);
        var request = ParseRequest(await ReadWrittenLineAsync(transport));
        transport.QueueIncoming(
            $"{{\"id\":{request.Id},\"error\":{{\"code\":-32602,\"message\":\"请求参数无效\"}}}}");

        var error = await Assert.ThrowsAsync<AppServerRequestException>(
            () => requestTask.WaitAsync(TestTimeout));
        Assert.Equal(-32602, error.Code);
        Assert.Equal("请求参数无效", error.Message);
    }

    [Fact]
    public async Task SuspiciousServerErrorMessageIsRedacted()
    {
        const string Secret = "sk-private-token-value";
        var transport = new FakeJsonLineTransport();
        await using var connection = new JsonRpcConnection(transport);
        await connection.StartAsync(CancellationToken.None);

        var requestTask = connection.RequestAsync("account/read", null, CancellationToken.None);
        var request = ParseRequest(await ReadWrittenLineAsync(transport));
        transport.QueueIncoming(
            $"{{\"id\":{request.Id},\"error\":{{\"code\":401,\"message\":\"Bearer {Secret}\"}}}}");

        var error = await Assert.ThrowsAsync<AppServerRequestException>(
            () => requestTask.WaitAsync(TestTimeout));
        Assert.Equal(401, error.Code);
        Assert.DoesNotContain(Secret, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NotificationIsRaisedFromBackgroundReadLoop()
    {
        var transport = new FakeJsonLineTransport();
        await using var connection = new JsonRpcConnection(transport);
        var notificationReceived = NewCompletionSource<JsonRpcNotification>();
        connection.NotificationReceived += (_, notification) =>
            notificationReceived.TrySetResult(notification);
        await connection.StartAsync(CancellationToken.None);
        await transport.WaitUntilReadingAsync(CancellationToken.None).WaitAsync(TestTimeout);

        transport.QueueIncoming(
            "{\"method\":\"account/rateLimits/updated\",\"params\":{\"source\":\"push\"}}");

        var notification = await notificationReceived.Task.WaitAsync(TestTimeout);
        Assert.Equal("account/rateLimits/updated", notification.Method);
        Assert.Equal("push", notification.Params.GetProperty("source").GetString());
    }

    [Fact]
    public async Task StartAsyncStartsTransportAndExactlyOneBackgroundReader()
    {
        var transport = new FakeJsonLineTransport();
        await using var connection = new JsonRpcConnection(transport);

        await Task.WhenAll(
            connection.StartAsync(CancellationToken.None),
            connection.StartAsync(CancellationToken.None));
        await transport.WaitUntilReadingAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal(1, transport.StartCount);
        Assert.Equal(1, transport.ReadCount);
    }

    [Fact]
    public async Task DisposeFailsPendingRequestsAndDisposesTransportOnce()
    {
        var transport = new FakeJsonLineTransport();
        var connection = new JsonRpcConnection(transport);
        await connection.StartAsync(CancellationToken.None);

        var pendingTask = connection.RequestAsync("never/responds", null, CancellationToken.None);
        _ = await ReadWrittenLineAsync(transport);

        await connection.DisposeAsync();
        await connection.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => pendingTask.WaitAsync(TestTimeout));
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => connection.RequestAsync("after/dispose", null, CancellationToken.None));
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task ProcessTransportSeparatesStdoutAndReportsOnlyRedactedStderrMetadata()
    {
        const string SecretStderr = "token=process-secret";
        var diagnosticReceived = NewCompletionSource<ProcessDiagnostic>();
        var launchSpec = new ProcessLaunchSpec(
            "powershell.exe",
            new[]
            {
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                $"$line = [Console]::In.ReadLine(); " +
                $"[Console]::Out.WriteLine($line); " +
                $"[Console]::Error.WriteLine('{SecretStderr}')",
            });
        await using var transport = new ProcessJsonLineTransport(
            launchSpec,
            diagnostic => diagnosticReceived.TrySetResult(diagnostic));
        using var timeout = new CancellationTokenSource(TestTimeout);

        await transport.StartAsync(timeout.Token);
        await transport.WriteLineAsync("stdout-message", timeout.Token);

        string? stdout = null;
        await foreach (var line in transport.ReadLinesAsync(timeout.Token))
        {
            stdout = line;
            break;
        }

        var diagnostic = await diagnosticReceived.Task.WaitAsync(TestTimeout);
        Assert.Equal("stdout-message", stdout);
        Assert.Equal("app_server_stderr", diagnostic.EventName);
        Assert.Equal(SecretStderr.Length, diagnostic.CharacterCount);
        Assert.DoesNotContain(SecretStderr, diagnostic.ToString(), StringComparison.Ordinal);
    }

    private static TaskCompletionSource<T> NewCompletionSource<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task<string> ReadWrittenLineAsync(FakeJsonLineTransport transport)
    {
        using var timeout = new CancellationTokenSource(TestTimeout);
        return await transport.ReadWrittenLineAsync(timeout.Token);
    }

    private static ParsedRequest ParseRequest(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        return new ParsedRequest(
            root.GetProperty("method").GetString()!,
            root.GetProperty("id").GetInt64());
    }

    private sealed record ParsedRequest(string Method, long Id);
}
