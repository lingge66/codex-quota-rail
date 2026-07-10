using System.Windows;
using CodexQuotaRail.App.Rail;
using CodexQuotaRail.Core.Quotas;
using CodexQuotaRail.Windows.Overlay;
using CodexQuotaRail.Windows.Windows;

namespace CodexQuotaRail.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var isRailPreview = e.Args.Contains("--rail-preview", StringComparer.OrdinalIgnoreCase);
        var isCompactPreview = e.Args.Contains(
            "--rail-preview-compact",
            StringComparer.OrdinalIgnoreCase);
        if (isRailPreview || isCompactPreview)
        {
            ShowRailPreview(
                isCompactPreview
                    ? OverlayMode.CompactTitleBar
                    : OverlayMode.ExternalRail);
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
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
        MainWindow = window;
        window.Show();
        window.QueuePlacement(
            new OverlayPlacement(
                new PixelRect(
                    80,
                    80,
                    1000,
                    mode == OverlayMode.CompactTitleBar ? 4 : 22),
                mode,
                1),
            dpiScale: 1);
    }
}
