using System.Text.Json;
using CodexQuotaRail.App.Diagnostics;

namespace CodexQuotaRail.App.Tests;

public sealed class JsonLineLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"CodexQuotaRail.Logs.{Guid.NewGuid():N}");

    [Fact]
    public async Task WriteAsyncRedactsSecretsAndWritesOnlySafeExceptionType()
    {
        var log = new JsonLineLog(
            _directory,
            timeProvider: new FixedTimeProvider(
                new DateTimeOffset(2026, 7, 10, 9, 30, 0, TimeSpan.FromHours(8))));
        const string message =
            "C:\\Users\\Alice\\secret.txt Authorization: Bearer bearer-secret " +
            "\"Authorization\":\"Bearer json-secret\" " +
            "accountId=acct_123 accessToken=access-secret refreshToken: refresh-secret " +
            "--token cli-secret";

        await log.WriteAsync(
            "error",
            "app_server_failed",
            message,
            new InvalidOperationException("exception-secret"));

        var line = Assert.Single(await File.ReadAllLinesAsync(Assert.Single(Directory.GetFiles(_directory))));
        Assert.DoesNotContain("Alice", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bearer-secret", line, StringComparison.Ordinal);
        Assert.DoesNotContain("json-secret", line, StringComparison.Ordinal);
        Assert.DoesNotContain("acct_123", line, StringComparison.Ordinal);
        Assert.DoesNotContain("access-secret", line, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-secret", line, StringComparison.Ordinal);
        Assert.DoesNotContain("cli-secret", line, StringComparison.Ordinal);
        Assert.DoesNotContain("exception-secret", line, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        Assert.Equal("error", root.GetProperty("level").GetString());
        Assert.Equal("app_server_failed", root.GetProperty("eventName").GetString());
        Assert.Equal("InvalidOperationException", root.GetProperty("exceptionType").GetString());
        Assert.Equal(5, root.EnumerateObject().Count());
    }

    [Fact]
    public async Task WriteAsyncRotatesAndRetainsOnlyThreeFiles()
    {
        var log = new JsonLineLog(
            _directory,
            maxFileBytes: 220,
            retainedFileCount: 3,
            timeProvider: new FixedTimeProvider(
                new DateTimeOffset(2026, 7, 10, 9, 30, 0, TimeSpan.FromHours(8))));

        for (var index = 0; index < 12; index++)
        {
            await log.WriteAsync(
                "info",
                $"event_{index}",
                new string((char)('a' + index), 90));
        }

        var files = Directory.GetFiles(_directory, "app-*.jsonl");
        Assert.Equal(3, files.Length);
        Assert.All(files, file => Assert.NotEmpty(File.ReadAllText(file)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;

        public override TimeZoneInfo LocalTimeZone { get; } =
            TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(8), "Test", "Test");
    }
}
