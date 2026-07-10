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

public interface ICodexDiscoveryProbe
{
    IReadOnlyList<string> InstalledPackageExecutablePaths { get; }

    IReadOnlyList<string> RunningExecutablePaths { get; }

    string? GetEnvironmentVariable(string name);

    bool IsExecutableFile(string path);
}

public sealed class CodexExecutableResolver
{
    private const string OverrideVariable = "CODEX_QUOTA_RAIL_CODEX_PATH";
    private readonly ICodexDiscoveryProbe _probe;

    public CodexExecutableResolver(ICodexDiscoveryProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        _probe = probe;
    }

    public CodexResolution Resolve()
    {
        var overridePath = _probe.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridePath) &&
            _probe.IsExecutableFile(overridePath) &&
            !IsSupportedExtension(overridePath))
        {
            return new CodexResolution.Unsupported(
                "Codex 路径必须指向受支持的 .exe 或 .cmd 文件。");
        }

        if (TryResolveCandidate(overridePath, out var overrideResult))
        {
            return overrideResult;
        }

        foreach (var candidate in EnumeratePathCandidates())
        {
            if (TryResolveCandidate(candidate, out var pathResult))
            {
                return pathResult;
            }
        }

        foreach (var candidate in _probe.RunningExecutablePaths)
        {
            if (TryResolveCandidate(candidate, out var runningResult))
            {
                return runningResult;
            }
        }

        foreach (var candidate in _probe.InstalledPackageExecutablePaths)
        {
            if (TryResolveCandidate(candidate, out var packageResult))
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
        return new ProcessLaunchSpec(
            found.FileName,
            [.. found.PrefixArguments, "app-server", "--listen", "stdio://"]);
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
                yield return Path.Combine(directory, $"codex{extension}");
            }
        }
    }

    private static string[] ParseExecutableExtensions(string? value)
    {
        var extensions = (value ?? ".EXE;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(extension =>
                extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase))
            .Select(extension => extension.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return extensions.Length == 0 ? [".EXE", ".CMD"] : extensions;
    }

    private bool TryResolveCandidate(
        string? candidate,
        out CodexResolution.Found result)
    {
        if (string.IsNullOrWhiteSpace(candidate) ||
            !_probe.IsExecutableFile(candidate) ||
            !IsSupportedExtension(candidate))
        {
            result = null!;
            return false;
        }

        if (Path.GetExtension(candidate).Equals(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            var commandInterpreter = _probe.GetEnvironmentVariable("ComSpec");
            if (string.IsNullOrWhiteSpace(commandInterpreter))
            {
                commandInterpreter = "cmd.exe";
            }

            result = new CodexResolution.Found(
                commandInterpreter,
                ["/d", "/s", "/c", candidate]);
            return true;
        }

        result = new CodexResolution.Found(candidate, []);
        return true;
    }

    private static bool IsSupportedExtension(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase);
    }
}
