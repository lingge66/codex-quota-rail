using CodexQuotaRail.AppServer.Discovery;

namespace CodexQuotaRail.AppServer.Tests;

public sealed class CodexExecutableResolverTests
{
    [Fact]
    public void ResolvePrefersExistingOverrideAndBuildsExactLaunchArguments()
    {
        // Given
        const string overridePath = @"C:\tools\codex.exe";
        var probe = new FakeDiscoveryProbe(
            new Dictionary<string, string?>
            {
                ["CODEX_QUOTA_RAIL_CODEX_PATH"] = overridePath,
                ["PATH"] = @"C:\path-bin",
                ["PATHEXT"] = ".EXE;.CMD",
            },
            [overridePath, @"C:\path-bin\codex.exe"]);
        var resolver = new CodexExecutableResolver(probe);

        // When
        var resolution = resolver.Resolve();
        var launch = CodexExecutableResolver.CreateAppServerLaunchSpec(
            Assert.IsType<CodexResolution.Found>(resolution));

        // Then
        Assert.Equal(overridePath, launch.FileName);
        Assert.Equal(["app-server", "--listen", "stdio://"], launch.Arguments);
    }

    [Fact]
    public void ResolveUsesPathextOrderAndWrapsCmdWithoutShellConcatenation()
    {
        // Given
        const string commandPath = @"C:\path-bin\codex.cmd";
        var probe = new FakeDiscoveryProbe(
            new Dictionary<string, string?>
            {
                ["PATH"] = @"C:\path-bin",
                ["PATHEXT"] = ".CMD;.EXE",
                ["ComSpec"] = @"C:\Windows\System32\cmd.exe",
            },
            [commandPath, @"C:\path-bin\codex.exe"]);
        var resolver = new CodexExecutableResolver(probe);

        // When
        var found = Assert.IsType<CodexResolution.Found>(resolver.Resolve());
        var launch = CodexExecutableResolver.CreateAppServerLaunchSpec(found);

        // Then
        Assert.Equal(@"C:\Windows\System32\cmd.exe", launch.FileName);
        Assert.Equal(
            ["/d", "/s", "/c", commandPath, "app-server", "--listen", "stdio://"],
            launch.Arguments);
    }

    [Fact]
    public void ResolveFallsBackFromInvalidOverrideToRunningThenPackageCandidates()
    {
        // Given
        const string runningPath = @"C:\Codex\resources\codex.exe";
        const string packagePath = @"C:\WindowsApps\OpenAI.Codex\codex.exe";
        var probe = new FakeDiscoveryProbe(
            new Dictionary<string, string?>
            {
                ["CODEX_QUOTA_RAIL_CODEX_PATH"] = @"C:\missing\codex.exe",
            },
            [runningPath, packagePath],
            [runningPath],
            [packagePath]);

        // When
        var first = Assert.IsType<CodexResolution.Found>(
            new CodexExecutableResolver(probe).Resolve());
        probe.ExecutableFiles.Remove(runningPath);
        var second = Assert.IsType<CodexResolution.Found>(
            new CodexExecutableResolver(probe).Resolve());

        // Then
        Assert.Equal(runningPath, first.FileName);
        Assert.Equal(packagePath, second.FileName);
    }

    [Fact]
    public void ResolveReturnsActionableMissingResultWithoutDownloading()
    {
        // Given
        var resolver = new CodexExecutableResolver(
            new FakeDiscoveryProbe(new Dictionary<string, string?>(), []));

        // When
        var resolution = resolver.Resolve();

        // Then
        var missing = Assert.IsType<CodexResolution.Missing>(resolution);
        Assert.Contains("Codex", missing.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveReturnsUnsupportedForExistingUnsafeOverrideType()
    {
        // Given
        const string scriptPath = @"C:\tools\codex.ps1";
        var probe = new FakeDiscoveryProbe(
            new Dictionary<string, string?>
            {
                ["CODEX_QUOTA_RAIL_CODEX_PATH"] = scriptPath,
            },
            [scriptPath]);

        // When
        var resolution = new CodexExecutableResolver(probe).Resolve();

        // Then
        var unsupported = Assert.IsType<CodexResolution.Unsupported>(resolution);
        Assert.Contains("exe", unsupported.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(scriptPath, unsupported.UserMessage, StringComparison.Ordinal);
    }

    private sealed class FakeDiscoveryProbe : ICodexDiscoveryProbe
    {
        private readonly IReadOnlyDictionary<string, string?> _environment;

        public FakeDiscoveryProbe(
            IReadOnlyDictionary<string, string?> environment,
            IEnumerable<string> executableFiles,
            IReadOnlyList<string>? runningPaths = null,
            IReadOnlyList<string>? packagePaths = null)
        {
            _environment = environment;
            ExecutableFiles = new HashSet<string>(
                executableFiles,
                StringComparer.OrdinalIgnoreCase);
            RunningExecutablePaths = runningPaths ?? [];
            InstalledPackageExecutablePaths = packagePaths ?? [];
        }

        public HashSet<string> ExecutableFiles { get; }

        public IReadOnlyList<string> RunningExecutablePaths { get; }

        public IReadOnlyList<string> InstalledPackageExecutablePaths { get; }

        public string? GetEnvironmentVariable(string name) =>
            _environment.TryGetValue(name, out var value) ? value : null;

        public bool IsExecutableFile(string path) => ExecutableFiles.Contains(path);
    }
}
