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

    public PowerShellCodexPackageRegistrationSource(TimeSpan? timeout = null)
    {
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

        return GetRegistrationsAsync().GetAwaiter().GetResult();
    }

    private async Task<IReadOnlyList<CodexPackageRegistration>> GetRegistrationsAsync()
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
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return [];
            }
        }
        catch (Exception error) when (
            error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return [];
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(_timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            TryKill(process);
            return [];
        }

        var output = await outputTask.ConfigureAwait(false);
        _ = await errorTask.ConfigureAwait(false);
        return process.ExitCode == 0 ? Parse(output) : [];
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

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
        catch
        {
        }
    }
}
