using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using CodexQuotaRail.App.Updates;

namespace CodexQuotaRail.App.Hosting;

public sealed class DesktopApplicationActions : IApplicationActions
{
    private static readonly HttpClient UpdateHttpClient = new();
    private static readonly Uri LingGeWebsite = new("https://lingge66.pages.dev/");
    private readonly string _logDirectory;
    private readonly Action<Uri> _openExternalUri;
    private readonly Action _requestExit;

    public DesktopApplicationActions(
        string logDirectory,
        Action requestExit,
        Action<Uri>? openExternalUri = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        ArgumentNullException.ThrowIfNull(requestExit);
        _logDirectory = Path.GetFullPath(logDirectory);
        _requestExit = requestExit;
        _openExternalUri = openExternalUri ?? OpenExternalUri;
    }

    public void CheckForUpdates() => _ = CheckForUpdatesAsync();

    public void OpenLogs()
    {
        Directory.CreateDirectory(_logDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(_logDirectory);
        _ = Process.Start(startInfo);
    }

    public void OpenLingGeWebsite() => _openExternalUri(LingGeWebsite);

    public void ShowTroubleshooting() => System.Windows.MessageBox.Show(
        "排查建议：\n1. 确认 Codex 已启动并已登录。\n2. 从托盘选择“立即刷新”。\n3. 如仍不可用，请打开日志目录。",
        "Codex 额度故障排查",
        MessageBoxButton.OK,
        MessageBoxImage.Information);

    public void RequestExit() => _requestExit();

    private static void OpenExternalUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("只允许使用 HTTPS 打开外部网站。");
        }

        _ = Process.Start(
            new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true,
            });
    }

    private static async Task CheckForUpdatesAsync()
    {
        try
        {
            var currentVersion = typeof(DesktopApplicationActions).Assembly.GetName().Version ??
                new Version(0, 1, 0);
            var result = await new GitHubReleaseChecker(UpdateHttpClient)
                .CheckAsync(currentVersion);
            if (result.Status != UpdateCheckStatus.UpdateAvailable ||
                result.LatestVersion is null ||
                result.ReleasePage is null)
            {
                System.Windows.MessageBox.Show(
                    result.Status == UpdateCheckStatus.NoStableRelease
                        ? "暂未找到可用的稳定版本。"
                        : $"当前已是最新版本（{currentVersion.ToString(3)}）。",
                    "Codex 可用额度",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var confirmation = System.Windows.MessageBox.Show(
                $"发现新版本 {result.LatestVersion.ToString(3)}，是否打开 GitHub Release 页面？",
                "Codex 可用额度",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (confirmation == MessageBoxResult.Yes)
            {
                _ = Process.Start(
                    new ProcessStartInfo(result.ReleasePage.AbsoluteUri)
                    {
                        UseShellExecute = true,
                    });
            }
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or TimeoutException)
        {
            System.Windows.MessageBox.Show(
                $"检查更新失败：{error.Message}",
                "Codex 可用额度",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
