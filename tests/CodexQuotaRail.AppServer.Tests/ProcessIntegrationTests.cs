using System.Diagnostics;
using System.Text.Json;
using CodexQuotaRail.AppServer.Protocol;
using CodexQuotaRail.AppServer.Transport;
using CodexQuotaRail.Core.Quotas;

namespace CodexQuotaRail.AppServer.Tests;

public sealed class ProcessIntegrationTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task VersionProbeExitsQuicklyForExecutableDiscovery()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = FakeServerPath(),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--version");
        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("无法启动假 App Server。");
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
            var output = await process.StandardOutput.ReadToEndAsync();

            Assert.Equal(0, process.ExitCode);
            Assert.Equal("fake-codex-app-server 1.0.0", output.Trim());
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    [Fact]
    public async Task InitializeResponseDeclaresWindowsAndPushNotificationIsEmitted()
    {
        await using var transport = new ProcessJsonLineTransport(
            Launch("healthy", "--emit-update"));
        using var cancellation = new CancellationTokenSource(TestTimeout);
        await transport.StartAsync(cancellation.Token);
        await using var output = transport.ReadLinesAsync(cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);

        await transport.WriteLineAsync(
            "{\"id\":1,\"method\":\"initialize\",\"params\":{}}",
            cancellation.Token);
        using var initialized = JsonDocument.Parse(await ReadLineAsync(output));
        var result = initialized.RootElement.GetProperty("result");
        Assert.Equal("fake-codex-app-server/1.0", result.GetProperty("userAgent").GetString());
        Assert.Equal("windows", result.GetProperty("platformFamily").GetString());
        Assert.Equal("windows", result.GetProperty("platformOs").GetString());

        await transport.WriteLineAsync(
            "{\"method\":\"initialized\",\"params\":{}}",
            cancellation.Token);
        await transport.WriteLineAsync(
            "{\"id\":2,\"method\":\"account/rateLimits/read\",\"params\":{}}",
            cancellation.Token);
        using var limits = JsonDocument.Parse(await ReadLineAsync(output));
        Assert.Equal(
            68,
            limits.RootElement.GetProperty("result")
                .GetProperty("rateLimits")
                .GetProperty("primary")
                .GetProperty("usedPercent")
                .GetInt32());
        using var notification = JsonDocument.Parse(await ReadLineAsync(output));
        Assert.Equal(
            "account/rateLimits/updated",
            notification.RootElement.GetProperty("method").GetString());
    }

    [Fact]
    public async Task HealthyFixtureFlowsThroughRealProcessClientAndNormalizer()
    {
        await using var connection = new JsonRpcConnection(
            new ProcessJsonLineTransport(Launch("healthy")));
        using var cancellation = new CancellationTokenSource(TestTimeout);
        await connection.StartAsync(cancellation.Token);
        await connection.InitializeAsync(new Version(0, 1, 0), cancellation.Token);

        var account = await connection.RequestAsync(
            "account/read",
            new { },
            cancellation.Token);
        var result = await connection.RequestAsync(
            "account/rateLimits/read",
            new { },
            cancellation.Token);
        var display = QuotaNormalizer.Normalize(
            RateLimitSnapshotMapper.Map(result, DateTimeOffset.UtcNow));

        Assert.Equal("chatgpt", account.GetProperty("account").GetProperty("type").GetString());
        Assert.Equal([32, 59], display.Windows.Select(window => window.AvailablePercent));
    }

    [Theory]
    [InlineData("single", 1, QuotaWindowState.Healthy)]
    [InlineData("unlimited", 1, QuotaWindowState.Unlimited)]
    public async Task AlternateFixturesMapDeterministically(
        string fixture,
        int expectedCount,
        QuotaWindowState expectedState)
    {
        await using var connection = new JsonRpcConnection(
            new ProcessJsonLineTransport(Launch(fixture)));
        using var cancellation = new CancellationTokenSource(TestTimeout);
        await connection.StartAsync(cancellation.Token);
        await connection.InitializeAsync(new Version(0, 1, 0), cancellation.Token);
        var result = await connection.RequestAsync(
            "account/rateLimits/read",
            new { },
            cancellation.Token);

        var display = QuotaNormalizer.Normalize(
            RateLimitSnapshotMapper.Map(result, DateTimeOffset.UtcNow));

        Assert.Equal(expectedCount, display.Windows.Count);
        Assert.Equal(expectedState, display.Windows[0].State);
    }

    [Fact]
    public async Task DisconnectAfterReadClosesStdoutAfterResponse()
    {
        await using var transport = new ProcessJsonLineTransport(
            Launch("healthy", "--disconnect-after-read"));
        using var cancellation = new CancellationTokenSource(TestTimeout);
        await transport.StartAsync(cancellation.Token);
        await using var output = transport.ReadLinesAsync(cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);
        await transport.WriteLineAsync(
            "{\"id\":1,\"method\":\"account/rateLimits/read\",\"params\":{}}",
            cancellation.Token);

        _ = await ReadLineAsync(output);

        Assert.False(await output.MoveNextAsync().AsTask().WaitAsync(TestTimeout));
    }

    private static ProcessLaunchSpec Launch(string fixture, params string[] extraArguments) =>
        new(FakeServerPath(), ["--fixture", fixture, .. extraArguments]);

    private static string FakeServerPath()
    {
        var root = FindRepositoryRoot();
        var configuration = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        return Path.Combine(
            root,
            "tools",
            "FakeCodexAppServer",
            "bin",
            configuration,
            "net8.0",
            OperatingSystem.IsWindows() ? "FakeCodexAppServer.exe" : "FakeCodexAppServer");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CodexQuotaRail.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("无法定位测试仓库根目录。");
    }

    private static async Task<string> ReadLineAsync(IAsyncEnumerator<string> output)
    {
        Assert.True(await output.MoveNextAsync().AsTask().WaitAsync(TestTimeout));
        return output.Current;
    }
}
