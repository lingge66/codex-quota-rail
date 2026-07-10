using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexQuotaRail.App.Diagnostics;

public sealed class JsonLineLog : IDisposable
{
    private const long DefaultMaxFileBytes = 5 * 1024 * 1024;
    private static readonly Regex UserPathPattern = new(
        @"\b[a-z]:\\Users\\[^\\\s""']+(?:\\[^\s""',;]*)*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AuthorizationPattern = new(
        @"(Authorization[""']?\s*[:=]\s*)(?:""[^""]*""|'[^']*'|(?:Bearer\s+)?[^\s,;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex NamedSecretPattern = new(
        @"\b(accessToken|refreshToken|accountId|account_id)\b([""']?\s*[:=]\s*)(?:""[^""]*""|'[^']*'|[^\s,;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CommandTokenPattern = new(
        @"(--(?:access-?token|refresh-?token|token)(?:\s+|=))(?:""[^""]*""|'[^']*'|[^\s]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AccountIdPattern = new(
        @"\b(?:acct|account)_[a-z0-9._-]+\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly string _directory;
    private readonly long _maxFileBytes;
    private readonly int _retainedFileCount;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonLineLog(
        string? directory = null,
        long maxFileBytes = DefaultMaxFileBytes,
        int retainedFileCount = 3,
        TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFileBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retainedFileCount);

        _directory = directory ?? GetDefaultDirectory();
        _maxFileBytes = maxFileBytes;
        _retainedFileCount = retainedFileCount;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask WriteAsync(
        string level,
        string eventName,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetLocalNow();
        var entry = new LogEntry(
            now,
            Redact(level),
            Redact(eventName),
            Redact(message),
            exception?.GetType().Name);
        var line = JsonSerializer.Serialize(entry, LogJsonContext.Default.LogEntry) + Environment.NewLine;
        var byteCount = Encoding.UTF8.GetByteCount(line);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_directory);
            var activePath = System.IO.Path.Combine(
                _directory,
                $"app-{now:yyyyMMdd}.jsonl");
            RotateIfNeeded(activePath, byteCount);
            await File.AppendAllTextAsync(
                activePath,
                line,
                Encoding.UTF8,
                cancellationToken).ConfigureAwait(false);
            PruneOldFiles();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    public static string GetDefaultDirectory() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexQuotaRail",
        "logs");

    internal static string Redact(string? value)
    {
        var safe = value ?? string.Empty;
        safe = UserPathPattern.Replace(safe, "[redacted-user-path]");
        safe = AuthorizationPattern.Replace(safe, "$1[redacted]");
        safe = NamedSecretPattern.Replace(safe, "$1$2[redacted]");
        safe = CommandTokenPattern.Replace(safe, "$1[redacted]");
        return AccountIdPattern.Replace(safe, "[redacted-account-id]");
    }

    private void RotateIfNeeded(string activePath, int nextEntryBytes)
    {
        var activeFile = new FileInfo(activePath);
        if (!activeFile.Exists || activeFile.Length == 0 ||
            activeFile.Length + nextEntryBytes <= _maxFileBytes)
        {
            return;
        }

        var stem = System.IO.Path.GetFileNameWithoutExtension(activePath);
        var sequence = 1;
        string archivePath;
        do
        {
            archivePath = System.IO.Path.Combine(
                _directory,
                $"{stem}-{sequence++:D3}.jsonl");
        }
        while (File.Exists(archivePath));

        File.Move(activePath, archivePath);
    }

    private void PruneOldFiles()
    {
        var files = new DirectoryInfo(_directory)
            .GetFiles("app-*.jsonl")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .Skip(_retainedFileCount);
        foreach (var file in files)
        {
            file.Delete();
        }
    }

    public sealed record LogEntry(
        DateTimeOffset Timestamp,
        string Level,
        string EventName,
        string Message,
        string? ExceptionType);
}

[System.Text.Json.Serialization.JsonSerializable(typeof(JsonLineLog.LogEntry))]
[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class LogJsonContext
    : System.Text.Json.Serialization.JsonSerializerContext;
