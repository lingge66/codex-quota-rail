using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;
using CodexQuotaRail.AppServer.Protocol;
using CodexQuotaRail.AppServer.Transport;

namespace CodexQuotaRail.AppServer.Tests;

public sealed class JsonRpcConnectionTests
{
    private static readonly IReadOnlyList<string> BlockingCleanupArguments =
    [
        "-NoProfile",
        "-NonInteractive",
        "-Command",
        "[Console]::Error.WriteLine('cleanup-blocked'); Start-Sleep -Seconds 30",
    ];

    private static readonly TimeSpan ImmediateFailureTimeout = TimeSpan.FromSeconds(1);
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
        Assert.Equal("App Server 请求失败。", error.Message);
        Assert.False(transport.TryReadWrittenLine(out _));
    }

    [Fact]
    public async Task PublicRequestBeforeInitializationIsRejectedWithoutWriting()
    {
        var transport = new FakeJsonLineTransport();
        await using var connection = new JsonRpcConnection(transport);
        await connection.StartAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource(ImmediateFailureTimeout);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => connection.RequestAsync("account/read", null, cancellation.Token));

        Assert.False(transport.TryReadWrittenLine(out _));
    }

    [Fact]
    public async Task PublicNotificationBeforeInitializationIsRejectedWithoutWriting()
    {
        var transport = new FakeJsonLineTransport();
        await using var connection = new JsonRpcConnection(transport);
        await connection.StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await connection.NotifyAsync(
                    "unsafe/notification",
                    null,
                    CancellationToken.None)
                .AsTask()
                .WaitAsync(ImmediateFailureTimeout));

        Assert.False(transport.TryReadWrittenLine(out _));
    }

    [Fact]
    public async Task ConcurrentInitializeIsRejectedWithoutSendingSecondRequest()
    {
        var transport = new FakeJsonLineTransport();
        await using var connection = new JsonRpcConnection(transport);
        await connection.StartAsync(CancellationToken.None);

        var firstInitialize = connection.InitializeAsync(new Version(0, 1, 0), CancellationToken.None);
        var firstRequest = ParseRequest(await ReadWrittenLineAsync(transport));
        using var secondCancellation = new CancellationTokenSource(ImmediateFailureTimeout);

        var secondError = await Record.ExceptionAsync(
            () => connection.InitializeAsync(new Version(0, 1, 0), secondCancellation.Token));
        var sentSecondRequest = transport.TryReadWrittenLine(out _);

        transport.QueueIncoming($"{{\"id\":{firstRequest.Id},\"result\":{{}}}}");
        await firstInitialize.WaitAsync(TestTimeout);
        _ = await ReadWrittenLineAsync(transport);

        Assert.IsType<InvalidOperationException>(secondError);
        Assert.False(sentSecondRequest);
    }

    [Fact]
    public async Task RepeatedInitializeAfterSuccessIsRejectedWithoutWriting()
    {
        var transport = new FakeJsonLineTransport();
        await using var connection = new JsonRpcConnection(transport);
        await StartAndInitializeAsync(connection, transport);
        using var cancellation = new CancellationTokenSource(ImmediateFailureTimeout);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => connection.InitializeAsync(new Version(0, 1, 0), cancellation.Token));

        Assert.False(transport.TryReadWrittenLine(out _));
    }

    [Fact]
    public async Task InitializeFailureMakesConnectionTerminal()
    {
        var transport = new FakeJsonLineTransport();
        await using var connection = new JsonRpcConnection(transport);
        await connection.StartAsync(CancellationToken.None);

        var initializeTask = connection.InitializeAsync(new Version(0, 1, 0), CancellationToken.None);
        var initializeRequest = ParseRequest(await ReadWrittenLineAsync(transport));
        transport.QueueIncoming(
            $"{{\"id\":{initializeRequest.Id},\"error\":{{\"code\":-32000,\"message\":\"初始化失败\"}}}}");

        var initializeError = await Assert.ThrowsAsync<AppServerRequestException>(
            () => initializeTask.WaitAsync(TestTimeout));
        using var cancellation = new CancellationTokenSource(ImmediateFailureTimeout);
        var terminalError = await Assert.ThrowsAsync<AppServerProtocolException>(
            () => connection.RequestAsync("account/read", null, cancellation.Token));

        Assert.Equal(-32000, initializeError.Code);
        Assert.Equal("App Server 初始化失败，连接不可继续使用。", terminalError.Message);
        Assert.False(transport.TryReadWrittenLine(out _));
    }

    [Fact]
    public async Task ConcurrentRequestsAreCorrelatedWhenResponsesArriveOutOfOrder()
    {
        var transport = new FakeJsonLineTransport();
        await using var connection = new JsonRpcConnection(transport);
        await StartAndInitializeAsync(connection, transport);

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
        await StartAndInitializeAsync(connection, transport);
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
        await StartAndInitializeAsync(connection, transport);

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
        await StartAndInitializeAsync(connection, transport);

        transport.QueueIncoming($"[\"{Secret}\"]");

        var error = await protocolError.Task.WaitAsync(TestTimeout);
        Assert.DoesNotContain(Secret, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private-credential", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServerErrorPreservesCodeAndUsesFixedSafeMessage()
    {
        var transport = new FakeJsonLineTransport();
        await using var connection = new JsonRpcConnection(transport);
        await StartAndInitializeAsync(connection, transport);

        var requestTask = connection.RequestAsync("account/read", null, CancellationToken.None);
        var request = ParseRequest(await ReadWrittenLineAsync(transport));
        transport.QueueIncoming(
            $"{{\"id\":{request.Id},\"error\":{{\"code\":-32602,\"message\":\"请求参数无效\"}}}}");

        var error = await Assert.ThrowsAsync<AppServerRequestException>(
            () => requestTask.WaitAsync(TestTimeout));
        Assert.Equal(-32602, error.Code);
        Assert.Equal("App Server 请求失败。", error.Message);
    }

    [Theory]
    [InlineData(@"C:\Users\Alice\AppData\Local\Codex\private.json")]
    [InlineData("account_01J9ZY8P4M7K2N6Q3R5T8V0WXY")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c")]
    [InlineData("a9F4c2B7e1D8f6A3c5E0b9D2f7A4e8C1b6D3f0A5c9E2b7D4f1A8c6E3b0D5f9A2")]
    public async Task ServerErrorNeverReturnsUnapprovedMessage(string serverMessage)
    {
        var transport = new FakeJsonLineTransport();
        await using var connection = new JsonRpcConnection(transport);
        await StartAndInitializeAsync(connection, transport);

        var requestTask = connection.RequestAsync("account/read", null, CancellationToken.None);
        var request = ParseRequest(await ReadWrittenLineAsync(transport));
        transport.QueueIncoming(
            JsonSerializer.Serialize(
                new
                {
                    id = request.Id,
                    error = new { code = 500, message = serverMessage },
                }));

        var error = await Assert.ThrowsAsync<AppServerRequestException>(
            () => requestTask.WaitAsync(TestTimeout));
        Assert.Equal(500, error.Code);
        Assert.Equal("App Server 请求失败。", error.Message);
        Assert.DoesNotContain(serverMessage, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuspiciousServerErrorMessageIsRedacted()
    {
        const string Secret = "sk-private-token-value";
        var transport = new FakeJsonLineTransport();
        await using var connection = new JsonRpcConnection(transport);
        await StartAndInitializeAsync(connection, transport);

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
        await StartAndInitializeAsync(connection, transport);
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
    public async Task RequestAfterEndOfStreamFailsImmediatelyWithTerminalError()
    {
        var transport = new FakeJsonLineTransport();
        await using var connection = new JsonRpcConnection(transport);
        var protocolError = NewCompletionSource<AppServerProtocolException>();
        connection.ProtocolError += (_, error) => protocolError.TrySetResult(error);
        await StartAndInitializeAsync(connection, transport);

        transport.CompleteIncoming();
        var terminalError = await protocolError.Task.WaitAsync(TestTimeout);

        var requestError = await Assert.ThrowsAsync<AppServerProtocolException>(
            () => connection.RequestAsync("after-eof/read", null, CancellationToken.None)
                .WaitAsync(ImmediateFailureTimeout));
        Assert.Equal("App Server 连接已关闭。", terminalError.Message);
        Assert.Equal(terminalError.Message, requestError.Message);
        Assert.False(transport.TryReadWrittenLine(out _));
    }

    [Fact]
    public async Task NotifyAfterReadFailureFailsImmediatelyWithSanitizedTerminalError()
    {
        const string Secret = "Bearer read-loop-secret";
        var transport = new FakeJsonLineTransport();
        await using var connection = new JsonRpcConnection(transport);
        var protocolError = NewCompletionSource<AppServerProtocolException>();
        connection.ProtocolError += (_, error) => protocolError.TrySetResult(error);
        await StartAndInitializeAsync(connection, transport);

        transport.CompleteIncoming(new IOException(Secret));
        var terminalError = await protocolError.Task.WaitAsync(TestTimeout);

        var notifyError = await Assert.ThrowsAsync<AppServerProtocolException>(
            async () => await connection.NotifyAsync(
                    "after-failure",
                    null,
                    CancellationToken.None)
                .AsTask()
                .WaitAsync(ImmediateFailureTimeout));
        Assert.Equal("读取 App Server 消息失败。", terminalError.Message);
        Assert.Equal(terminalError.Message, notifyError.Message);
        Assert.DoesNotContain(Secret, notifyError.Message, StringComparison.Ordinal);
        Assert.False(transport.TryReadWrittenLine(out _));
    }

    [Fact]
    public async Task DisposeFailsPendingRequestsAndDisposesTransportOnce()
    {
        var transport = new FakeJsonLineTransport();
        var connection = new JsonRpcConnection(transport);
        await StartAndInitializeAsync(connection, transport);

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
    public async Task ConcurrentDisposeCallsWaitForSharedCleanupTask()
    {
        var transport = new FakeJsonLineTransport { PauseDispose = true };
        var connection = new JsonRpcConnection(transport);
        await connection.StartAsync(CancellationToken.None);

        var firstDispose = connection.DisposeAsync().AsTask();
        await transport.WaitUntilDisposeStartsAsync(CancellationToken.None).WaitAsync(TestTimeout);
        var secondDispose = connection.DisposeAsync().AsTask();
        var firstCompletedEarly = firstDispose.IsCompleted;
        var secondCompletedEarly = secondDispose.IsCompleted;

        transport.ReleaseDispose();
        await Task.WhenAll(firstDispose, secondDispose).WaitAsync(TestTimeout);

        Assert.False(firstCompletedEarly);
        Assert.False(secondCompletedEarly);
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task DisposeWrapsWin32FailureAndAllCallersObserveSameSafeError()
    {
        const string Secret = @"C:\Users\Alice\AppData\Local\account-123456789";
        var transport = new FakeJsonLineTransport
        {
            DisposeException = new Win32Exception(Secret),
        };
        var connection = new JsonRpcConnection(transport);
        await connection.StartAsync(CancellationToken.None);

        var firstError = await Record.ExceptionAsync(
            async () => await connection.DisposeAsync());
        var secondError = await Record.ExceptionAsync(
            async () => await connection.DisposeAsync());

        var firstProtocolError = Assert.IsType<AppServerProtocolException>(firstError);
        var secondProtocolError = Assert.IsType<AppServerProtocolException>(secondError);
        Assert.Same(firstProtocolError, secondProtocolError);
        Assert.Equal("清理 App Server 连接失败。", firstProtocolError.Message);
        Assert.DoesNotContain(Secret, firstProtocolError.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task DisposeWaitsForInFlightStartAndDoesNotPublishReader()
    {
        var transport = new FakeJsonLineTransport { PauseStart = true };
        var connection = new JsonRpcConnection(transport);

        var startTask = connection.StartAsync(CancellationToken.None);
        await transport.WaitUntilStartEntersAsync(CancellationToken.None).WaitAsync(TestTimeout);
        var disposeTask = connection.DisposeAsync().AsTask();
        var disposeCompletedBeforeStart = disposeTask.IsCompleted;

        transport.ReleaseStart();
        var startError = await Record.ExceptionAsync(() => startTask);
        await disposeTask.WaitAsync(TestTimeout);

        Assert.False(disposeCompletedBeforeStart);
        Assert.IsType<ObjectDisposedException>(startError);
        Assert.Equal(0, transport.ReadCount);
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

    [Fact]
    public async Task ProcessTransportConcurrentDisposeCallsWaitForSameCleanup()
    {
        using var releaseDiagnostic = new ManualResetEventSlim();
        var diagnosticEntered = NewCompletionSource();
        var launchSpec = new ProcessLaunchSpec(
            "powershell.exe",
            BlockingCleanupArguments);
        var transport = new ProcessJsonLineTransport(
            launchSpec,
            _ =>
            {
                diagnosticEntered.TrySetResult();
                releaseDiagnostic.Wait();
            });
        using var timeout = new CancellationTokenSource(TestTimeout);
        Task? firstDispose = null;
        Task? secondDispose = null;

        try
        {
            await transport.StartAsync(timeout.Token);
            await diagnosticEntered.Task.WaitAsync(TestTimeout);

            firstDispose = transport.DisposeAsync().AsTask();
            secondDispose = transport.DisposeAsync().AsTask();
            var secondCompletedEarly = secondDispose.IsCompleted;

            releaseDiagnostic.Set();
            await Task.WhenAll(firstDispose, secondDispose).WaitAsync(TestTimeout);

            Assert.False(secondCompletedEarly);
        }
        finally
        {
            releaseDiagnostic.Set();
            if (firstDispose is not null)
            {
                await firstDispose.WaitAsync(TestTimeout);
            }

            if (secondDispose is not null)
            {
                await secondDispose.WaitAsync(TestTimeout);
            }

            await transport.DisposeAsync();
        }
    }

    private static TaskCompletionSource<T> NewCompletionSource<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource NewCompletionSource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task StartAndInitializeAsync(
        JsonRpcConnection connection,
        FakeJsonLineTransport transport)
    {
        await connection.StartAsync(CancellationToken.None);
        var initializeTask = connection.InitializeAsync(new Version(0, 1, 0), CancellationToken.None);
        var request = ParseRequest(await ReadWrittenLineAsync(transport));
        Assert.Equal("initialize", request.Method);
        transport.QueueIncoming($"{{\"id\":{request.Id},\"result\":{{}}}}");
        await initializeTask.WaitAsync(TestTimeout);

        using var initialized = JsonDocument.Parse(await ReadWrittenLineAsync(transport));
        Assert.Equal("initialized", initialized.RootElement.GetProperty("method").GetString());
    }

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
