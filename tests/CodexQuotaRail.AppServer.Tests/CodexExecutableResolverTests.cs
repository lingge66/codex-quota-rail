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
    public void ResolveUsesPathextOrderAndBuildsValidatedSingleCmdCommand()
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
            [
                "/d",
                "/s",
                "/v:off",
                "/c",
                $"\"{commandPath}\" \"app-server\" \"--listen\" \"stdio://\"",
            ],
            launch.Arguments);
    }

    [Fact]
    public void ResolveFallsBackFromInvalidOverrideToRunningThenPackageCandidates()
    {
        // Given
        const string runningRoot = @"C:\WindowsApps\OpenAI.Codex.Running";
        const string packageRoot = @"C:\WindowsApps\OpenAI.Codex.Installed";
        var runningPath = Path.Combine(runningRoot, "app", "resources", "codex.exe");
        var packagePath = Path.Combine(packageRoot, "app", "resources", "codex.exe");
        var probe = new FakeDiscoveryProbe(
            new Dictionary<string, string?>
            {
                ["CODEX_QUOTA_RAIL_CODEX_PATH"] = @"C:\missing\codex.exe",
            },
            [runningPath, packagePath],
            [runningPath],
            [
                new CodexPackageRegistration("OpenAI.Codex", runningRoot),
                new CodexPackageRegistration("OpenAI.Codex", packageRoot),
            ]);

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

    [Fact]
    public void ResolveUsesOfficialRegisteredPackageWithoutPathOrRunningProcess()
    {
        // Given
        const string packageRoot = @"C:\Program Files\WindowsApps\OpenAI.Codex_1.0_x64";
        var executable = Path.Combine(packageRoot, "app", "resources", "codex.exe");
        var probe = new PackageOnlyProbe(
            [new CodexPackageRegistration("OpenAI.Codex", packageRoot)],
            [executable]);

        // When
        var resolution = new CodexExecutableResolver(probe).Resolve();

        // Then
        var found = Assert.IsType<CodexResolution.Found>(resolution);
        Assert.Equal(executable, found.FileName);
    }

    [Fact]
    public void ResolveRejectsLookalikePackageIdentityAndEscapingCanonicalTarget()
    {
        // Given
        const string packageRoot = @"C:\Program Files\WindowsApps\OpenAI.Codex_1.0_x64";
        var packageExecutable = Path.Combine(packageRoot, "app", "resources", "codex.exe");
        const string outsideTarget = @"C:\Temp\codex.exe";
        var probe = new PackageOnlyProbe(
            [
                new CodexPackageRegistration("OpenAI.Codex.Fake", packageRoot),
                new CodexPackageRegistration("OpenAI.Codex", packageRoot),
            ],
            [outsideTarget],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [packageExecutable] = outsideTarget,
            });

        // When
        var resolution = new CodexExecutableResolver(probe).Resolve();

        // Then
        Assert.IsType<CodexResolution.Missing>(resolution);
    }

    private sealed class FakeDiscoveryProbe : ICodexDiscoveryProbe
    {
        private readonly IReadOnlyDictionary<string, string?> _environment;

        public FakeDiscoveryProbe(
            IReadOnlyDictionary<string, string?> environment,
            IEnumerable<string> executableFiles,
            IReadOnlyList<string>? runningPaths = null,
            IReadOnlyList<CodexPackageRegistration>? registeredPackages = null)
        {
            _environment = environment;
            ExecutableFiles = new HashSet<string>(
                executableFiles,
                StringComparer.OrdinalIgnoreCase);
            RunningExecutablePaths = runningPaths ?? [];
            RegisteredPackages = registeredPackages ?? [];
        }

        public HashSet<string> ExecutableFiles { get; }

        public IReadOnlyList<string> RunningExecutablePaths { get; }

        public IReadOnlyList<CodexPackageRegistration> RegisteredPackages { get; }

        public string? GetEnvironmentVariable(string name) =>
            _environment.TryGetValue(name, out var value) ? value : null;

        public string? GetCanonicalPath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception error) when (
                error is ArgumentException or IOException or NotSupportedException)
            {
                return null;
            }
        }

        public bool IsExecutableFile(string path) => ExecutableFiles.Contains(path);
    }

    private sealed class PackageOnlyProbe : ICodexDiscoveryProbe
    {
        private readonly IReadOnlyDictionary<string, string> _canonicalPaths;
        private readonly HashSet<string> _executableFiles;

        public PackageOnlyProbe(
            IReadOnlyList<CodexPackageRegistration> registrations,
            IEnumerable<string> executableFiles,
            IReadOnlyDictionary<string, string>? canonicalPaths = null)
        {
            RegisteredPackages = registrations;
            _executableFiles = new HashSet<string>(
                executableFiles,
                StringComparer.OrdinalIgnoreCase);
            _canonicalPaths = canonicalPaths ?? new Dictionary<string, string>();
        }

        public IReadOnlyList<CodexPackageRegistration> RegisteredPackages { get; }

        public IReadOnlyList<string> RunningExecutablePaths => [];

        public string? GetCanonicalPath(string path) =>
            _canonicalPaths.TryGetValue(path, out var target)
                ? target
                : Path.GetFullPath(path);

        public string? GetEnvironmentVariable(string name) => null;

        public bool IsExecutableFile(string path) => _executableFiles.Contains(path);
    }
}
