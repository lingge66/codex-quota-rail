using System.Net;
using System.Text;
using CodexQuotaRail.App.Updates;

namespace CodexQuotaRail.App.Tests;

public sealed class GitHubReleaseCheckerTests
{
    [Fact]
    public async Task RequestsOfficialLatestReleaseWithDescriptiveUserAgent()
    {
        var handler = new StubHttpMessageHandler(
            """
            {"tag_name":"v0.2.0","html_url":"https://github.com/lingge66/codex-quota-rail/releases/tag/v0.2.0","draft":false,"prerelease":false}
            """);
        using var client = new HttpClient(handler);
        var checker = new GitHubReleaseChecker(client);

        var result = await checker.CheckAsync(new Version(0, 1, 0));

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal(new Version(0, 2, 0), result.LatestVersion);
        Assert.Equal(
            "https://api.github.com/repos/lingge66/codex-quota-rail/releases/latest",
            handler.Request?.RequestUri?.AbsoluteUri);
        Assert.Contains(
            handler.Request!.Headers.UserAgent,
            item => item.Product?.Name == "CodexQuotaRail");
    }

    [Fact]
    public async Task NeverSelectsDraftOrPrereleaseAsAnAutomaticUpdate()
    {
        var handler = new StubHttpMessageHandler(
            """
            {"tag_name":"v1.0.0-beta.1","html_url":"https://example.invalid/release","draft":false,"prerelease":true}
            """);
        using var client = new HttpClient(handler);
        var checker = new GitHubReleaseChecker(client);

        var result = await checker.CheckAsync(new Version(0, 1, 0));

        Assert.Equal(UpdateCheckStatus.NoStableRelease, result.Status);
        Assert.Null(result.ReleasePage);
    }

    [Fact]
    public async Task CancelsAStalledRequestAtConfiguredTimeout()
    {
        using var client = new HttpClient(new BlockingHttpMessageHandler());
        var checker = new GitHubReleaseChecker(client, TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAsync<TimeoutException>(
            () => checker.CheckAsync(new Version(0, 1, 0)));
    }

    private sealed class StubHttpMessageHandler(string json) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                });
        }
    }

    private sealed class BlockingHttpMessageHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("不可到达");
        }
    }
}
