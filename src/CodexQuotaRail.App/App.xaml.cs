using System.Windows;
using CodexQuotaRail.App.Hosting;
using CodexQuotaRail.App.Rail;
using CodexQuotaRail.App.Settings;
using CodexQuotaRail.Core.Quotas;
using CodexQuotaRail.Windows.Overlay;
using CodexQuotaRail.Windows.Windows;

namespace CodexQuotaRail.App;

public partial class App : System.Windows.Application
{
    private ApplicationHost? _host;
    private SingleInstanceGuard? _instanceGuard;
    private int _shutdownStarted;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (TryShowPreview(e.Args))
        {
            return;
        }

        _instanceGuard = SingleInstanceGuard.Acquire(
            () => Dispatcher.BeginInvoke(() => _ = HandleActivationAsync()));
        if (!_instanceGuard.IsPrimary)
        {
            _ = await _instanceGuard.SignalPrimaryAsync(TimeSpan.FromSeconds(2));
            await _instanceGuard.DisposeAsync();
            _instanceGuard = null;
            Shutdown(0);
            return;
        }

        try
        {
            _host = BuildApplicationHost();
            await _host.StartAsync(CancellationToken.None);
        }
        catch (UnsupportedSettingsSchemaException error)
        {
            System.Windows.MessageBox.Show(
                error.Message,
                "Codex 额度设置不兼容",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            await ShutdownAndExitAsync(1);
        }
        catch (Exception)
        {
            System.Windows.MessageBox.Show(
                "Codex 额度显示启动失败。请从日志目录查看脱敏诊断信息。",
                "Codex 可用额度",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            await ShutdownAndExitAsync(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _host?.ShutdownAsync().GetAwaiter().GetResult();
            _instanceGuard?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        finally
        {
            _host = null;
            _instanceGuard = null;
            base.OnExit(e);
        }
    }

    private async Task HandleActivationAsync()
    {
        var host = _host;
        if (host is null)
        {
            return;
        }

        try
        {
            await host.RequestRefreshAsync(CancellationToken.None);
        }
        catch (Exception)
        {
        }
    }

    private void RequestExit() => _ = ShutdownAndExitAsync(0);

    private async Task ShutdownAndExitAsync(int exitCode)
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
        {
            return;
        }

        try
        {
            if (_host is not null)
            {
                await _host.ShutdownAsync();
                _host = null;
            }

            if (_instanceGuard is not null)
            {
                await _instanceGuard.DisposeAsync();
                _instanceGuard = null;
            }
        }
        finally
        {
            Shutdown(exitCode);
        }
    }

    private bool TryShowPreview(IReadOnlyCollection<string> args)
    {
        var external = args.Contains("--rail-preview", StringComparer.OrdinalIgnoreCase);
        var compact = args.Contains("--rail-preview-compact", StringComparer.OrdinalIgnoreCase);
        if (!external && !compact)
        {
            return false;
        }

        ShowRailPreview(compact ? OverlayMode.CompactTitleBar : OverlayMode.ExternalRail);
        return true;
    }

    private void ShowRailPreview(OverlayMode mode)
    {
        var now = DateTimeOffset.Now;
        var state = new QuotaDisplayState(
            [
                new QuotaWindowDisplay(
                    "5 小时",
                    68,
                    TimeSpan.FromHours(5),
                    now.AddHours(2).AddMinutes(14),
                    QuotaWindowState.Healthy),
                new QuotaWindowDisplay(
                    "本周",
                    34,
                    TimeSpan.FromDays(7),
                    now.AddDays(3).AddHours(8),
                    QuotaWindowState.Notice),
            ],
            QuotaConnectionState.Live,
            now,
            null);
        var viewModel = new RailViewModel();
        viewModel.Apply(state, mode);
        var window = new RailWindow(viewModel);
        window.Closed += (_, _) => Shutdown();
        MainWindow = window;
        window.Show();
        window.QueuePlacement(
            new OverlayPlacement(
                new PixelRect(80, 80, 1000, mode == OverlayMode.CompactTitleBar ? 4 : 22),
                mode,
                1),
            dpiScale: 1);
    }
}
