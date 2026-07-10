using CodexQuotaRail.AppServer.Discovery;

namespace CodexQuotaRail.AppServer.Tests;

public sealed class SystemCodexDiscoveryProbeTests
{
    private static readonly TimeSpan PackageTtl = TimeSpan.FromSeconds(5);

    [Fact]
    public void EmptyPackageSnapshotRetriesImmediatelyAfterInstall()
    {
        // Given
        var source = new MutablePackageSource([]);
        var time = new ManualTimeProvider();
        var probe = new SystemCodexDiscoveryProbe(source, time, PackageTtl);
        Assert.Empty(probe.RegisteredPackages);
        source.Registrations = [Package("installed")];

        // When
        var packages = probe.RegisteredPackages;

        // Then
        Assert.Single(packages);
        Assert.Equal(2, source.CallCount);
    }

    [Fact]
    public void NonEmptyPackageSnapshotRefreshesAfterTtlSoDeletionCanFallBack()
    {
        // Given
        var source = new MutablePackageSource([Package("installed")]);
        var time = new ManualTimeProvider();
        var probe = new SystemCodexDiscoveryProbe(source, time, PackageTtl);
        Assert.Single(probe.RegisteredPackages);
        source.Registrations = [];
        Assert.Single(probe.RegisteredPackages);

        // When
        time.Advance(PackageTtl);
        var packages = probe.RegisteredPackages;

        // Then
        Assert.Empty(packages);
        Assert.Equal(2, source.CallCount);
    }

    [Fact]
    public async Task FailedExecutableProbeRecoversAfterFileIsInstalled()
    {
        // Given
        var path = CreateTemporaryCommandPath();
        var probe = new SystemCodexDiscoveryProbe(new MutablePackageSource([]));
        Assert.False(probe.IsExecutableFile(path));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            await File.WriteAllTextAsync(path, "@echo off\r\n");

            // When
            var executable = probe.IsExecutableFile(path);

            // Then
            Assert.True(executable);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public async Task SuccessfulExecutableProbeExpiresWhenFileIsDeleted()
    {
        // Given
        var path = CreateTemporaryCommandPath();
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(path, "@echo off\r\n");
            var probe = new SystemCodexDiscoveryProbe(new MutablePackageSource([]));
            Assert.True(probe.IsExecutableFile(path));

            // When
            File.Delete(path);
            var executable = probe.IsExecutableFile(path);

            // Then
            Assert.False(executable);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExecutableStartFailureIsRetriedAndCanRecover()
    {
        // Given
        var path = CreateTemporaryExecutablePath("codex.exe");
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(path, string.Empty);
            var runner = new SequenceRunner(
                Result(started: false, exited: false, exitCode: null),
                Result(started: true, exited: true, exitCode: 0));
            var probe = new SystemCodexDiscoveryProbe(
                new MutablePackageSource([]),
                new ManualTimeProvider(),
                PackageTtl,
                runner);
            Assert.False(probe.IsExecutableFile(path));

            // When
            var executable = probe.IsExecutableFile(path);

            // Then
            Assert.True(executable);
            Assert.Equal(2, runner.CallCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static CodexPackageRegistration Package(string suffix) =>
        new("OpenAI.Codex", $@"C:\WindowsApps\OpenAI.Codex.{suffix}");

    private static string CreateTemporaryCommandPath() => CreateTemporaryExecutablePath("codex.cmd");

    private static string CreateTemporaryExecutablePath(string fileName) =>
        Path.Combine(
            Path.GetTempPath(),
            $"CodexQuotaRail-probe-{Guid.NewGuid():N}",
            fileName);

    private static BoundedProcessResult Result(bool started, bool exited, int? exitCode) =>
        new(
            started,
            exited,
            TimedOut: false,
            exitCode,
            string.Empty,
            string.Empty,
            StandardOutputTruncated: false,
            StandardErrorTruncated: false);

    private sealed class MutablePackageSource(
        IReadOnlyList<CodexPackageRegistration> registrations)
        : ICodexPackageRegistrationSource
    {
        public int CallCount { get; private set; }

        public IReadOnlyList<CodexPackageRegistration> Registrations { get; set; } = registrations;

        public IReadOnlyList<CodexPackageRegistration> GetRegistrations()
        {
            CallCount++;
            return Registrations;
        }
    }

    private sealed class SequenceRunner(params BoundedProcessResult[] results)
        : IBoundedProcessRunner
    {
        public int CallCount { get; private set; }

        public BoundedProcessResult Run(
            System.Diagnostics.ProcessStartInfo startInfo,
            TimeSpan timeout) => results[CallCount++];
    }
}
