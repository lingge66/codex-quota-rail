using System.Diagnostics;
using System.Reflection;
using CodexQuotaRail.AppServer.Discovery;

namespace CodexQuotaRail.AppServer.Tests;

public sealed class PowerShellCodexPackageRegistrationSourceTests
{
    [Fact]
    public void GetRegistrationsParsesNormalBoundedRunnerOutput()
    {
        // Given
        var runner = new StubRunner(
            new BoundedProcessResult(
                Started: true,
                Exited: true,
                TimedOut: false,
                ExitCode: 0,
                StandardOutput: "OpenAI.Codex|C:\\WindowsApps\\OpenAI.Codex\r\n",
                StandardError: string.Empty,
                StandardOutputTruncated: false,
                StandardErrorTruncated: false));
        var source = new PowerShellCodexPackageRegistrationSource(
            runner,
            TimeSpan.FromSeconds(1));

        // When
        var registrations = source.GetRegistrations();

        // Then
        var registration = Assert.Single(registrations);
        Assert.Equal("OpenAI.Codex", registration.IdentityName);
        Assert.Equal(1, runner.CallCount);
        Assert.Contains("-NonInteractive", runner.StartInfo!.ArgumentList);
    }

    [Fact]
    public void ParseAcceptsLiteralPipeSeparatorUsedByPowerShellQuery()
    {
        var parse = typeof(PowerShellCodexPackageRegistrationSource).GetMethod(
            "Parse",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(parse);

        var result = parse.Invoke(null, ["OpenAI.Codex|C:\\Program Files\\WindowsApps\\OpenAI.Codex"]);
        var registrations = Assert.IsAssignableFrom<IReadOnlyList<CodexPackageRegistration>>(result);
        var registration = Assert.Single(registrations);

        Assert.Equal("OpenAI.Codex", registration.IdentityName);
        Assert.Equal(
            "C:\\Program Files\\WindowsApps\\OpenAI.Codex",
            registration.InstallLocation);
    }

    private sealed class StubRunner(BoundedProcessResult result) : IBoundedProcessRunner
    {
        public int CallCount { get; private set; }

        public ProcessStartInfo? StartInfo { get; private set; }

        public BoundedProcessResult Run(ProcessStartInfo startInfo, TimeSpan timeout)
        {
            CallCount++;
            StartInfo = startInfo;
            return result;
        }
    }
}
