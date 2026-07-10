using System.Reflection;
using CodexQuotaRail.AppServer.Discovery;

namespace CodexQuotaRail.AppServer.Tests;

public sealed class PowerShellCodexPackageRegistrationSourceTests
{
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
}
