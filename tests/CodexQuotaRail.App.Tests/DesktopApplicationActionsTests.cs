using CodexQuotaRail.App.Hosting;

namespace CodexQuotaRail.App.Tests;

public sealed class DesktopApplicationActionsTests
{
    [Fact]
    public void OpenLingGeWebsiteUsesTheFixedHttpsAddress()
    {
        Uri? openedUri = null;
        var actions = new DesktopApplicationActions(
            Path.GetTempPath(),
            () => { },
            uri => openedUri = uri);

        actions.OpenLingGeWebsite();

        Assert.Equal(new Uri("https://lingge66.pages.dev/"), openedUri);
        Assert.Equal(Uri.UriSchemeHttps, openedUri!.Scheme);
    }
}
