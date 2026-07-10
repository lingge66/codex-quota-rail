using System.Globalization;
using CodexQuotaRail.App.Settings;

namespace CodexQuotaRail.App.Tray;

public sealed record TrayState(
    string StatusText,
    DateTimeOffset? UpdatedAt,
    bool FollowPaused,
    ThemePreference Theme,
    bool ReduceMotion,
    bool StartWithWindows);

public sealed record TrayMenuItemModel(
    string Id,
    string Text,
    bool IsEnabled = true,
    bool IsChecked = false,
    bool IsSeparator = false,
    IReadOnlyList<TrayMenuItemModel>? ChildItems = null)
{
    public IReadOnlyList<TrayMenuItemModel> Children { get; } = ChildItems ?? [];
}

public enum TrayCommand
{
    Refresh,
    SetFollowPaused,
    SetTheme,
    SetReduceMotion,
    SetAutostart,
    CheckUpdates,
    OpenLogs,
    Troubleshoot,
    Exit,
}

public sealed record TrayCommandRequest(
    TrayCommand Command,
    bool? BooleanValue = null,
    ThemePreference? Theme = null);

public interface ITrayIconHost : IDisposable
{
    event EventHandler<string>? CommandInvoked;

    void SetMenu(IReadOnlyList<TrayMenuItemModel> menu);
}

public sealed class TrayIconService : IDisposable
{
    private readonly ITrayIconHost _host;
    private TrayState _state = new(
        "正在连接 Codex",
        null,
        FollowPaused: false,
        ThemePreference.Automatic,
        ReduceMotion: false,
        StartWithWindows: true);
    private int _disposed;

    public TrayIconService()
        : this(new WindowsFormsTrayIconHost())
    {
    }

    public TrayIconService(ITrayIconHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _host.CommandInvoked += OnCommandInvoked;
        _host.SetMenu(BuildMenu(_state));
    }

    public event EventHandler<TrayCommandRequest>? CommandRequested;

    public void UpdateState(TrayState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _state = state;
        _host.SetMenu(BuildMenu(state));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _host.CommandInvoked -= OnCommandInvoked;
        _host.Dispose();
    }

    private static IReadOnlyList<TrayMenuItemModel> BuildMenu(TrayState state)
    {
        var status = state.UpdatedAt is { } updatedAt
            ? $"状态：{state.StatusText} · {updatedAt.ToString("HH:mm", CultureInfo.InvariantCulture)}"
            : $"状态：{state.StatusText}";
        return
        [
            new("status", status, IsEnabled: false),
            new("refresh", "立即刷新"),
            new("toggle-follow", state.FollowPaused ? "恢复窗口跟随" : "暂停窗口跟随"),
            Separator("separator-1"),
            new(
                "theme",
                "主题",
                ChildItems:
                [
                    new("theme-automatic", "自动", IsChecked: state.Theme == ThemePreference.Automatic),
                    new("theme-dark", "深色", IsChecked: state.Theme == ThemePreference.Dark),
                    new("theme-light", "浅色", IsChecked: state.Theme == ThemePreference.Light),
                ]),
            new("toggle-reduce-motion", "减少动画", IsChecked: state.ReduceMotion),
            new("toggle-autostart", "开机时启动", IsChecked: state.StartWithWindows),
            Separator("separator-2"),
            new("check-updates", "检查更新"),
            new("open-logs", "打开日志目录"),
            new("troubleshoot", "故障排查"),
            Separator("separator-3"),
            new("exit", "退出"),
        ];
    }

    private static TrayMenuItemModel Separator(string id) =>
        new(id, string.Empty, IsEnabled: false, IsSeparator: true);

    private void OnCommandInvoked(object? sender, string commandId)
    {
        var request = commandId switch
        {
            "refresh" => new TrayCommandRequest(TrayCommand.Refresh),
            "toggle-follow" => new TrayCommandRequest(
                TrayCommand.SetFollowPaused,
                BooleanValue: !_state.FollowPaused),
            "theme-automatic" => ThemeRequest(ThemePreference.Automatic),
            "theme-dark" => ThemeRequest(ThemePreference.Dark),
            "theme-light" => ThemeRequest(ThemePreference.Light),
            "toggle-reduce-motion" => new TrayCommandRequest(
                TrayCommand.SetReduceMotion,
                BooleanValue: !_state.ReduceMotion),
            "toggle-autostart" => new TrayCommandRequest(
                TrayCommand.SetAutostart,
                BooleanValue: !_state.StartWithWindows),
            "check-updates" => new TrayCommandRequest(TrayCommand.CheckUpdates),
            "open-logs" => new TrayCommandRequest(TrayCommand.OpenLogs),
            "troubleshoot" => new TrayCommandRequest(TrayCommand.Troubleshoot),
            "exit" => new TrayCommandRequest(TrayCommand.Exit),
            _ => null,
        };
        if (request is not null)
        {
            CommandRequested?.Invoke(this, request);
        }
    }

    private static TrayCommandRequest ThemeRequest(ThemePreference theme) =>
        new(TrayCommand.SetTheme, Theme: theme);
}
