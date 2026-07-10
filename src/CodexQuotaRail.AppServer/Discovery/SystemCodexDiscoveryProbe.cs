using System.Diagnostics;

namespace CodexQuotaRail.AppServer.Discovery;

public sealed class SystemCodexDiscoveryProbe : ICodexDiscoveryProbe
{
    private static readonly TimeSpan DefaultPackageTtl = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _packageTtl;
    private readonly ICodexPackageRegistrationSource _packageRegistrationSource;
    private readonly object _packageSync = new();
    private readonly TimeProvider _timeProvider;
    private readonly IBoundedProcessRunner _runner;
    private DateTimeOffset _packageExpiresAt = DateTimeOffset.MinValue;
    private IReadOnlyList<CodexPackageRegistration> _registeredPackages = [];

    public SystemCodexDiscoveryProbe(
        ICodexPackageRegistrationSource? packageRegistrationSource = null,
        TimeProvider? timeProvider = null,
        TimeSpan? packageTtl = null)
        : this(
            packageRegistrationSource,
            timeProvider,
            packageTtl,
            new BoundedProcessRunner())
    {
    }

    internal SystemCodexDiscoveryProbe(
        ICodexPackageRegistrationSource? packageRegistrationSource,
        TimeProvider? timeProvider,
        TimeSpan? packageTtl,
        IBoundedProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _packageRegistrationSource = packageRegistrationSource ??
            new PowerShellCodexPackageRegistrationSource();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _runner = runner;
        _packageTtl = packageTtl ?? DefaultPackageTtl;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_packageTtl, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(_packageTtl, TimeSpan.FromMinutes(1));
    }

    public IReadOnlyList<CodexPackageRegistration> RegisteredPackages =>
        GetRegisteredPackages();

    public IReadOnlyList<string> RunningExecutablePaths => GetRunningExecutablePaths();

    public string? GetCanonicalPath(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            {
                return fullPath;
            }

            var target = File.ResolveLinkTarget(fullPath, returnFinalTarget: true);
            return target is null ? fullPath : Path.GetFullPath(target.FullName);
        }
        catch (Exception error) when (
            error is ArgumentException or IOException or NotSupportedException or
                System.Security.SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public string? GetEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name);

    public bool FileExists(string path) => File.Exists(path);

    public bool IsExecutableFile(string path) => CanExecuteFile(path);

    private IReadOnlyList<CodexPackageRegistration> GetRegisteredPackages()
    {
        lock (_packageSync)
        {
            var now = _timeProvider.GetUtcNow();
            if (_registeredPackages.Count > 0 && now < _packageExpiresAt)
            {
                return _registeredPackages;
            }

            IReadOnlyList<CodexPackageRegistration> registrations;
            try
            {
                registrations = [.. _packageRegistrationSource.GetRegistrations()];
            }
            catch
            {
                registrations = [];
            }

            _registeredPackages = registrations;
            _packageExpiresAt = registrations.Count == 0
                ? now
                : now + _packageTtl;
            return _registeredPackages;
        }
    }

    private bool CanExecuteFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (Path.GetExtension(path).Equals(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--version");
        return _runner.Run(startInfo, TimeSpan.FromSeconds(3)).Succeeded;
    }

    private static List<string> GetRunningExecutablePaths()
    {
        var paths = new List<string>();
        foreach (var process in Process.GetProcessesByName("Codex"))
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        paths.Add(path);
                    }
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
            }
        }

        return paths;
    }
}
