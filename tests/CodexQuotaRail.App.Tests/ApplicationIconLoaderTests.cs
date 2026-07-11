using CodexQuotaRail.App.Tray;

namespace CodexQuotaRail.App.Tests;

public sealed class ApplicationIconLoaderTests
{
    [Fact]
    public void LoadFallsBackToAUsableOwnedIconWhenExecutableIsMissing()
    {
        using var icon = ApplicationIconLoader.Load("Z:\\missing\\CodexQuotaRail.App.exe");

        Assert.True(icon.Width > 0);
        Assert.True(icon.Height > 0);
    }
}
