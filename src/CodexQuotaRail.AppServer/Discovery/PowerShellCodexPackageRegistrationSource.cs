using System.Diagnostics;

namespace CodexQuotaRail.AppServer.Discovery;

public interface ICodexPackageRegistrationSource
{
    IReadOnlyList<CodexPackageRegistration> GetRegistrations();
}

public sealed class PowerShellCodexPackageRegistrationSource : ICodexPackageRegistrationSource
{
    private const string OutputSeparator = "|";
    private const string Query =
        "$packages=Get-AppxPackage -Name 'OpenAI.Codex' -ErrorAction SilentlyContinue;" +
        "foreach($package in $packages){" +
        "[Console]::Out.WriteLine($package.Name+'" + OutputSeparator +
        "'+$package.InstallLocation)}";
    private readonly TimeSpan _timeout;
    private readonly IBoundedProcessRunner _runner;

    public PowerShellCodexPackageRegistrationSource(TimeSpan? timeout = null)
        : this(new BoundedProcessRunner(), timeout)
    {
    }

    internal PowerShellCodexPackageRegistrationSource(
        IBoundedProcessRunner runner,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_timeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            _timeout,
            TimeSpan.FromSeconds(30));
    }

    public IReadOnlyList<CodexPackageRegistration> GetRegistrations()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        return GetRegistrationsCore();
    }

    private List<CodexPackageRegistration> GetRegistrationsCore()
    {
        var executable = Path.Combine(
            Environment.SystemDirectory,
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(Query);
        var result = _runner.Run(startInfo, _timeout);
        return result.Succeeded && !result.StandardOutputTruncated
            ? Parse(result.StandardOutput)
            : [];
    }

    private static List<CodexPackageRegistration> Parse(string output)
    {
        var registrations = new List<CodexPackageRegistration>();
        foreach (var line in output.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf(OutputSeparator, StringComparison.Ordinal);
            if (separator <= 0 || separator == line.Length - 1)
            {
                continue;
            }

            var identity = line[..separator];
            var installLocation = line[(separator + 1)..];
            if (identity.Equals("OpenAI.Codex", StringComparison.OrdinalIgnoreCase))
            {
                registrations.Add(new CodexPackageRegistration(identity, installLocation));
            }
        }

        return registrations;
    }
}
