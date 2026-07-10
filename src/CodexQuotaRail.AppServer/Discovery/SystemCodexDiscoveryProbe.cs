using System.Collections.Concurrent;
using System.Diagnostics;

namespace CodexQuotaRail.AppServer.Discovery;

public sealed class SystemCodexDiscoveryProbe : ICodexDiscoveryProbe
{
    private readonly ConcurrentDictionary<string, bool> _executableCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<IReadOnlyList<CodexPackageRegistration>> _registeredPackages;

    public SystemCodexDiscoveryProbe(
        ICodexPackageRegistrationSource? packageRegistrationSource = null)
    {
        var source = packageRegistrationSource ??
            new PowerShellCodexPackageRegistrationSource();
        _registeredPackages = new Lazy<IReadOnlyList<CodexPackageRegistration>>(
            source.GetRegistrations,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IReadOnlyList<CodexPackageRegistration> RegisteredPackages =>
        _registeredPackages.Value;

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

    public bool IsExecutableFile(string path) =>
        _executableCache.GetOrAdd(path, CanExecuteFile);

    private static bool CanExecuteFile(string path)
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
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return false;
            }

            _ = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(3000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception error) when (
            error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
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
