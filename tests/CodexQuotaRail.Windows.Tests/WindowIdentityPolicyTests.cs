using CodexQuotaRail.Windows.Windows;

namespace CodexQuotaRail.Windows.Tests;

public sealed class WindowIdentityPolicyTests
{
    [Theory]
    [InlineData(@"C:\Tools\Codex.exe", null, true)]
    [InlineData(@"C:\Tools\codex.EXE", null, true)]
    [InlineData(@"C:\Windows\explorer.exe", null, false)]
    [InlineData(@"C:\Apps\Editor.exe", null, false)]
    [InlineData(@"C:\Tools\Codex.exe", "OpenAI.Codex_1.0_x64__test", false)]
    public void AuthenticodeRunsOnlyForUnpackagedCodexExecutable(
        string path,
        string? packageFullName,
        bool expected)
    {
        Assert.Equal(
            expected,
            CodexExecutableIdentityPolicy.RequiresSignerLookup(path, packageFullName));
    }
}
