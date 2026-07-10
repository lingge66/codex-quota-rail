using CodexQuotaRail.AppServer.Discovery;

namespace CodexQuotaRail.AppServer.Tests;

public sealed class CmdLaunchSecurityTests
{
    [Fact]
    public async Task CmdShimExecutesExactArgumentsFromQuotedMetacharacterPath()
    {
        // Given
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"Codex & safe space {Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var shimPath = Path.Combine(directory, "codex.cmd");
            await File.WriteAllTextAsync(
                shimPath,
                "@echo off\r\necho [%~1][%~2][%~3]\r\n",
                timeout.Token);
            var probe = new RealCommandProbe(shimPath);
            var found = Assert.IsType<CodexResolution.Found>(
                new CodexExecutableResolver(probe).Resolve());
            var launch = CodexExecutableResolver.CreateAppServerLaunchSpec(found);

            // When
            var result = await RunAsync(launch, timeout.Token);

            // Then
            Assert.True(
                result.Lines.SequenceEqual(["[app-server][--listen][stdio://]"]) &&
                result.DiagnosticCount == 0,
                "cmd.exe 必须只执行已引用的 shim 并原样传递固定参数。");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("C:\\unsafe%TEMP%\\codex.cmd")]
    [InlineData("C:\\unsafe!name\\codex.cmd")]
    [InlineData("C:\\unsafe\"name\\codex.cmd")]
    [InlineData("C:\\unsafe\r\nname\\codex.cmd")]
    [InlineData("C:\\unsafe\0name\\codex.cmd")]
    public void CmdShimRejectsPathsThatCannotBeSafelyRepresented(string unsafePath)
    {
        // Given
        var resolver = new CodexExecutableResolver(new UnsafeCommandProbe(unsafePath));

        // When
        var resolution = resolver.Resolve();

        // Then
        var unsupported = Assert.IsType<CodexResolution.Unsupported>(resolution);
        Assert.DoesNotContain(unsafePath, unsupported.UserMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void CmdShimRejectsMissingCommandInterpreter()
    {
        // Given
        var resolver = new CodexExecutableResolver(new MissingInterpreterProbe());

        // When
        var resolution = resolver.Resolve();

        // Then
        Assert.IsType<CodexResolution.Unsupported>(resolution);
    }

    private static async Task<ProcessResult> RunAsync(
        CodexQuotaRail.AppServer.Transport.ProcessLaunchSpec launch,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<CodexQuotaRail.AppServer.Transport.ProcessDiagnostic>();
        await using var transport = new CodexQuotaRail.AppServer.Transport.ProcessJsonLineTransport(
            launch,
            diagnostics.Add);
        await transport.StartAsync(cancellationToken);
        var lines = new List<string>();
        await foreach (var line in transport.ReadLinesAsync(cancellationToken))
        {
            lines.Add(line);
        }

        return new ProcessResult(lines, diagnostics.Count);
    }

    private sealed class RealCommandProbe(string shimPath) : ICodexDiscoveryProbe
    {
        public IReadOnlyList<CodexPackageRegistration> RegisteredPackages => [];

        public IReadOnlyList<string> RunningExecutablePaths => [];

        public string? GetCanonicalPath(string path) => Path.GetFullPath(path);

        public string? GetEnvironmentVariable(string name) => name switch
        {
            "CODEX_QUOTA_RAIL_CODEX_PATH" => shimPath,
            "ComSpec" => Environment.GetEnvironmentVariable("ComSpec"),
            _ => null,
        };

        public bool FileExists(string path) => File.Exists(path);

        public bool IsExecutableFile(string path) => File.Exists(path);
    }

    private sealed class UnsafeCommandProbe(string unsafePath) : ICodexDiscoveryProbe
    {
        public IReadOnlyList<CodexPackageRegistration> RegisteredPackages => [];

        public IReadOnlyList<string> RunningExecutablePaths => [];

        public string? GetCanonicalPath(string path) => path;

        public string? GetEnvironmentVariable(string name) => name switch
        {
            "CODEX_QUOTA_RAIL_CODEX_PATH" => unsafePath,
            "ComSpec" => @"C:\Windows\System32\cmd.exe",
            _ => null,
        };

        public bool FileExists(string path) => true;

        public bool IsExecutableFile(string path) => true;
    }

    private sealed class MissingInterpreterProbe : ICodexDiscoveryProbe
    {
        private const string CommandPath = @"C:\tools\codex.cmd";

        public IReadOnlyList<CodexPackageRegistration> RegisteredPackages => [];

        public IReadOnlyList<string> RunningExecutablePaths => [];

        public string? GetCanonicalPath(string path) => Path.GetFullPath(path);

        public string? GetEnvironmentVariable(string name) => name switch
        {
            "CODEX_QUOTA_RAIL_CODEX_PATH" => CommandPath,
            "ComSpec" => @"C:\missing\cmd.exe",
            _ => null,
        };

        public bool FileExists(string path) => path == CommandPath;

        public bool IsExecutableFile(string path) => path == CommandPath;
    }

    private sealed record ProcessResult(
        IReadOnlyList<string> Lines,
        int DiagnosticCount);
}
