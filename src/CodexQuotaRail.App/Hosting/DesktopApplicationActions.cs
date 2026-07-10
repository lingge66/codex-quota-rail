using System.Diagnostics;
using System.IO;
using System.Windows;

namespace CodexQuotaRail.App.Hosting;

public sealed class DesktopApplicationActions : IApplicationActions
{
    private readonly string _logDirectory;
    private readonly Action _requestExit;

    public DesktopApplicationActions(string logDirectory, Action requestExit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        ArgumentNullException.ThrowIfNull(requestExit);
        _logDirectory = Path.GetFullPath(logDirectory);
        _requestExit = requestExit;
    }

    public void CheckForUpdates() => System.Windows.MessageBox.Show(
        "当前是本地开发版本。正式发布后将通过 GitHub Release 安全检查更新。",
        "Codex 可用额度",
        MessageBoxButton.OK,
        MessageBoxImage.Information);

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

    public void ShowTroubleshooting() => System.Windows.MessageBox.Show(
        "排查建议：\n1. 确认 Codex 已启动并已登录。\n2. 从托盘选择“立即刷新”。\n3. 如仍不可用，请打开日志目录。",
        "Codex 额度故障排查",
        MessageBoxButton.OK,
        MessageBoxImage.Information);

    public void RequestExit() => _requestExit();
}
