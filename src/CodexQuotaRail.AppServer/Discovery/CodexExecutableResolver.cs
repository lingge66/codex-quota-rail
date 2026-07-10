using CodexQuotaRail.AppServer.Transport;

namespace CodexQuotaRail.AppServer.Discovery;

public abstract record CodexResolution
{
    public sealed record Found(
        string FileName,
        IReadOnlyList<string> PrefixArguments) : CodexResolution;

    public sealed record Missing(string UserMessage) : CodexResolution;

    public sealed record Unsupported(string UserMessage) : CodexResolution;
}

public sealed record CodexPackageRegistration(
    string IdentityName,
    string InstallLocation);

public interface ICodexDiscoveryProbe
{
    IReadOnlyList<CodexPackageRegistration> RegisteredPackages { get; }

    IReadOnlyList<string> RunningExecutablePaths { get; }

    string? GetCanonicalPath(string path);

    string? GetEnvironmentVariable(string name);

    bool FileExists(string path);

    bool IsExecutableFile(string path);
}

public sealed partial class CodexExecutableResolver
{
    private const string OfficialPackageIdentity = "OpenAI.Codex";
    private const string OverrideVariable = "CODEX_QUOTA_RAIL_CODEX_PATH";
    private readonly ICodexDiscoveryProbe _probe;

    public CodexExecutableResolver(ICodexDiscoveryProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        _probe = probe;
    }

    public CodexResolution Resolve()
    {
        var overrideResult = ResolveCandidate(
            _probe.GetEnvironmentVariable(OverrideVariable),
            reportUnsupported: true);
        if (overrideResult is not null)
        {
            return overrideResult;
        }

        foreach (var candidate in EnumeratePathCandidates())
        {
            if (ResolveCandidate(candidate, reportUnsupported: false) is { } pathResult)
            {
                return pathResult;
            }
        }

        var packageCandidates = GetOfficialPackageCandidates();
        foreach (var runningPath in _probe.RunningExecutablePaths)
        {
            var canonicalRunningPath = _probe.GetCanonicalPath(runningPath);
            if (canonicalRunningPath is null ||
                !packageCandidates.Contains(
                    canonicalRunningPath,
                    StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ResolveCandidate(canonicalRunningPath, reportUnsupported: false) is { } runningResult)
            {
                return runningResult;
            }
        }

        foreach (var candidate in packageCandidates)
        {
            if (ResolveCandidate(candidate, reportUnsupported: false) is { } packageResult)
            {
                return packageResult;
            }
        }

        return new CodexResolution.Missing(
            "未找到可用的 Codex，请先安装或启动 Codex。不会自动下载或抓取界面。");
    }

    public static ProcessLaunchSpec CreateAppServerLaunchSpec(CodexResolution.Found found)
    {
        ArgumentNullException.ThrowIfNull(found);
        if (IsCompleteCmdLaunch(found))
        {
            var command = found.PrefixArguments[4];
            var completeCommandLine = $"/d /s /v:off /c \"{command}\"";
            return ProcessLaunchSpec.FromCompleteCommandLine(
                found.FileName,
                found.PrefixArguments,
                completeCommandLine);
        }

        return new ProcessLaunchSpec(
            found.FileName,
            [.. found.PrefixArguments, "app-server", "--listen", "stdio://"]);
    }

    private CodexResolution? ResolveCandidate(string? path, bool reportUnsupported)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var canonicalPath = _probe.GetCanonicalPath(path);
        if (canonicalPath is null)
        {
            return null;
        }

        if (!_probe.FileExists(canonicalPath))
        {
            return null;
        }

        var extension = Path.GetExtension(canonicalPath);
        if (!IsSupportedExtension(extension))
        {
            return reportUnsupported
                ? new CodexResolution.Unsupported(
                    "Codex 路径必须指向受支持的 .exe 或 .cmd 文件。")
                : null;
        }

        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) &&
            !CanSafelyQuoteCmdToken(canonicalPath))
        {
            return reportUnsupported
                ? new CodexResolution.Unsupported(
                    "Codex 命令文件路径包含无法安全处理的字符。")
                : null;
        }

        if (!_probe.IsExecutableFile(canonicalPath))
        {
            return null;
        }

        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            var commandInterpreter = ResolveCommandInterpreter();
            return commandInterpreter is null
                ? new CodexResolution.Unsupported("当前系统无法安全启动 Codex 命令文件。")
                : new CodexResolution.Found(
                    commandInterpreter,
                    [
                        "/d",
                        "/s",
                        "/v:off",
                        "/c",
                        BuildCmdCommand(canonicalPath),
                    ]);
        }

        return new CodexResolution.Found(canonicalPath, []);
    }

    private string? ResolveCommandInterpreter()
    {
        var systemRoot = _probe.GetEnvironmentVariable("SystemRoot");
        var candidate = string.IsNullOrWhiteSpace(systemRoot)
            ? _probe.GetEnvironmentVariable("ComSpec")
            : Path.Combine(systemRoot, "System32", "cmd.exe");
        var canonicalPath = string.IsNullOrWhiteSpace(candidate)
            ? null
            : _probe.GetCanonicalPath(candidate);
        return canonicalPath is not null &&
            _probe.FileExists(canonicalPath) &&
            Path.GetFileName(canonicalPath).Equals("cmd.exe", StringComparison.OrdinalIgnoreCase)
                ? canonicalPath
                : null;
    }

    private IEnumerable<string> EnumeratePathCandidates()
    {
        var path = _probe.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            yield break;
        }

        var extensions = ParseExecutableExtensions(
            _probe.GetEnvironmentVariable("PATHEXT"));
        foreach (var directoryValue in path.Split(Path.PathSeparator))
        {
            var directory = directoryValue.Trim().Trim('"');
            if (directory.Length == 0)
            {
                continue;
            }

            foreach (var extension in extensions)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(directory, $"codex{extension}");
                }
                catch (ArgumentException)
                {
                    continue;
                }

                yield return candidate;
            }
        }
    }

    private static string[] ParseExecutableExtensions(string? value)
    {
        var extensions = (value ?? ".EXE;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsSupportedExtension)
            .Select(extension => extension.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return extensions.Length == 0 ? [".exe", ".cmd"] : extensions;
    }

    private static bool IsSupportedExtension(string extension) =>
        extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase);

    private static bool CanSafelyQuoteCmdToken(string token) =>
        token.IndexOfAny(['%', '!', '"', '\r', '\n', '\0']) < 0;

    private static string BuildCmdCommand(string commandPath)
    {
        var tokens = new[]
        {
            commandPath,
            "app-server",
            "--listen",
            "stdio://",
        };
        if (tokens.Any(token => !CanSafelyQuoteCmdToken(token)))
        {
            throw new InvalidOperationException("无法安全构造 Codex 命令行。");
        }

        return string.Join(' ', tokens.Select(token => $"\"{token}\""));
    }

    private static bool IsCompleteCmdLaunch(CodexResolution.Found found) =>
        Path.GetFileName(found.FileName).Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) &&
        found.PrefixArguments.Count == 5 &&
        found.PrefixArguments[0].Equals("/d", StringComparison.OrdinalIgnoreCase) &&
        found.PrefixArguments[1].Equals("/s", StringComparison.OrdinalIgnoreCase) &&
        found.PrefixArguments[2].Equals("/v:off", StringComparison.OrdinalIgnoreCase) &&
        found.PrefixArguments[3].Equals("/c", StringComparison.OrdinalIgnoreCase);

}
