using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CodexQuotaRail.App.Updates;

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    NoStableRelease,
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    Version? LatestVersion = null,
    Uri? ReleasePage = null);

public sealed class GitHubReleaseChecker
{
    public static readonly Uri LatestReleaseEndpoint = new(
        "https://api.github.com/repos/lingge66/codex-quota-rail/releases/latest");

    private readonly HttpClient _client;
    private readonly TimeSpan _timeout;

    public GitHubReleaseChecker(HttpClient client, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
        if (_timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    public async Task<UpdateCheckResult> CheckAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseEndpoint);
        request.Headers.UserAgent.Add(
            new ProductInfoHeaderValue("CodexQuotaRail", currentVersion.ToString(3)));
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            using var response = await _client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token)
                .ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: timeout.Token).ConfigureAwait(false);
            return Parse(document.RootElement, currentVersion);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("检查更新超过 10 秒，请稍后重试。", error);
        }
    }

    private static UpdateCheckResult Parse(JsonElement root, Version currentVersion)
    {
        if (ReadBoolean(root, "draft") || ReadBoolean(root, "prerelease"))
        {
            return new UpdateCheckResult(UpdateCheckStatus.NoStableRelease);
        }

        var tag = ReadRequiredString(root, "tag_name").Trim();
        var versionText = tag.StartsWith('v') ? tag[1..] : tag;
        if (!Version.TryParse(versionText, out var latest))
        {
            return new UpdateCheckResult(UpdateCheckStatus.NoStableRelease);
        }

        var releasePageText = ReadRequiredString(root, "html_url");
        if (!Uri.TryCreate(releasePageText, UriKind.Absolute, out var releasePage) ||
            releasePage.Scheme != Uri.UriSchemeHttps)
        {
            throw new JsonException("GitHub Release 页面地址无效。");
        }

        return latest > currentVersion
            ? new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, latest, releasePage)
            : new UpdateCheckResult(UpdateCheckStatus.UpToDate, latest, releasePage);
    }

    private static bool ReadBoolean(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;

    private static string ReadRequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new JsonException($"GitHub Release 缺少 {propertyName}。");
        }

        return value.GetString()!;
    }
}
