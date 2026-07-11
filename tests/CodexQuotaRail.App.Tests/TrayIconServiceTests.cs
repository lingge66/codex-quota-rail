using CodexQuotaRail.App.Settings;
using CodexQuotaRail.App.Tray;

namespace CodexQuotaRail.App.Tests;

public sealed class TrayIconServiceTests
{
    [Fact]
    public void UpdateStateBuildsChineseMenuAndCheckedSettings()
    {
        var host = new FakeTrayIconHost();
        using var service = new TrayIconService(host);

        service.UpdateState(
            new TrayState(
                "额度已更新",
                new DateTimeOffset(2026, 7, 10, 9, 30, 0, TimeSpan.FromHours(8)),
                FollowPaused: true,
                ThemePreference.Dark,
                ReduceMotion: true,
                StartWithWindows: true));

        Assert.Equal("状态：额度已更新 · 09:30", host.Menu[0].Text);
        Assert.Contains(host.Menu, item => item.Text == "立即刷新");
        Assert.Contains(host.Menu, item => item.Text == "恢复窗口跟随");
        var theme = Assert.Single(host.Menu, item => item.Text == "主题");
        Assert.True(Assert.Single(theme.Children, item => item.Text == "深色").IsChecked);
        Assert.True(Assert.Single(host.Menu, item => item.Text == "减少动画").IsChecked);
        Assert.True(Assert.Single(host.Menu, item => item.Text == "开机时启动").IsChecked);
        var websiteIndex = host.Menu.ToList().FindIndex(item => item.Id == "open-lingge-website");
        var troubleshootIndex = host.Menu.ToList().FindIndex(item => item.Id == "troubleshoot");
        var exitIndex = host.Menu.ToList().FindIndex(item => item.Id == "exit");
        Assert.Equal("领哥个人网站", host.Menu[websiteIndex].Text);
        Assert.True(troubleshootIndex < websiteIndex);
        Assert.True(websiteIndex < exitIndex);
        Assert.Equal("退出", host.Menu[^1].Text);
    }

    [Fact]
    public void ToggleCommandsEmitRequestedSettingValuesImmediately()
    {
        var host = new FakeTrayIconHost();
        using var service = new TrayIconService(host);
        service.UpdateState(
            new TrayState(
                "正在连接 Codex",
                null,
                FollowPaused: false,
                ThemePreference.Automatic,
                ReduceMotion: false,
                StartWithWindows: true));
        var requests = new List<TrayCommandRequest>();
        service.CommandRequested += (_, request) => requests.Add(request);

        host.Invoke("toggle-follow");
        host.Invoke("theme-light");
        host.Invoke("toggle-reduce-motion");
        host.Invoke("toggle-autostart");

        Assert.Collection(
            requests,
            request => Assert.Equal(true, request.BooleanValue),
            request => Assert.Equal(ThemePreference.Light, request.Theme),
            request => Assert.Equal(true, request.BooleanValue),
            request => Assert.Equal(false, request.BooleanValue));
    }

    [Fact]
    public void EveryClickableMenuEntryEmitsTheExpectedCommand()
    {
        var host = new FakeTrayIconHost();
        using var service = new TrayIconService(host);
        var requests = new List<TrayCommandRequest>();
        service.CommandRequested += (_, request) => requests.Add(request);

        host.Invoke("refresh");
        host.Invoke("toggle-follow");
        host.Invoke("theme-automatic");
        host.Invoke("theme-dark");
        host.Invoke("theme-light");
        host.Invoke("toggle-reduce-motion");
        host.Invoke("toggle-autostart");
        host.Invoke("check-updates");
        host.Invoke("open-logs");
        host.Invoke("troubleshoot");
        host.Invoke("open-lingge-website");
        host.Invoke("exit");

        Assert.Equal(
            [
                TrayCommand.Refresh,
                TrayCommand.SetFollowPaused,
                TrayCommand.SetTheme,
                TrayCommand.SetTheme,
                TrayCommand.SetTheme,
                TrayCommand.SetReduceMotion,
                TrayCommand.SetAutostart,
                TrayCommand.CheckUpdates,
                TrayCommand.OpenLogs,
                TrayCommand.Troubleshoot,
                TrayCommand.OpenLingGeWebsite,
                TrayCommand.Exit,
            ],
            requests.Select(request => request.Command));
        Assert.Equal(
            [
                ThemePreference.Automatic,
                ThemePreference.Dark,
                ThemePreference.Light,
            ],
            requests
                .Where(request => request.Command == TrayCommand.SetTheme)
                .Select(request => request.Theme));
    }

    private sealed class FakeTrayIconHost : ITrayIconHost
    {
        public event EventHandler<string>? CommandInvoked;

        public IReadOnlyList<TrayMenuItemModel> Menu { get; private set; } = [];

        public void Dispose()
        {
        }

        public void SetMenu(IReadOnlyList<TrayMenuItemModel> menu) => Menu = menu;

        public void RecreateIcon()
        {
        }

        public void Invoke(string commandId) => CommandInvoked?.Invoke(this, commandId);
    }
}
