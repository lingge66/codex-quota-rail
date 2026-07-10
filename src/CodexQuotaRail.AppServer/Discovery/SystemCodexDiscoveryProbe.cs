using System.Diagnostics;

namespace CodexQuotaRail.AppServer.Discovery;

public sealed class SystemCodexDiscoveryProbe : ICodexDiscoveryProbe
{
    public IReadOnlyList<string> RunningExecutablePaths => GetRunningExecutablePaths();

    public IReadOnlyList<string> InstalledPackageExecutablePaths =>
        GetInstalledPackageExecutablePaths();

    public string? GetEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name);

    public bool IsExecutableFile(string path) => File.Exists(path);

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

    private static IReadOnlyList<string> GetInstalledPackageExecutablePaths()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return [];
        }

        return
        [
            Path.Combine(localAppData, "Microsoft", "WindowsApps", "codex.exe"),
            Path.Combine(localAppData, "Microsoft", "WindowsApps", "codex.cmd"),
            Path.Combine(localAppData, "Programs", "Codex", "resources", "codex.exe"),
        ];
    }
}
