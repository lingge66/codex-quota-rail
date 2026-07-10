using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using CodexQuotaRail.App.Diagnostics;
using CodexQuotaRail.App.Hosting;
using CodexQuotaRail.App.Rail;
using CodexQuotaRail.App.Settings;
using CodexQuotaRail.App.Tray;
using CodexQuotaRail.AppServer.Discovery;
using CodexQuotaRail.AppServer.RateLimits;
using CodexQuotaRail.AppServer.Transport;
using CodexQuotaRail.Windows.Startup;
using CodexQuotaRail.Windows.Windows;

namespace CodexQuotaRail.App;

public partial class App
{
    private ApplicationHost BuildApplicationHost()
    {
        var log = new JsonLineLog();
        var settingsStore = new JsonSettingsStore();
        var viewModel = new RailViewModel();
        var railWindow = new RailWindow(viewModel);
        var railRenderer = new RailQuotaRenderer(viewModel, Dispatcher);
        var overlay = new RailOverlayPresenter(railWindow, railRenderer, viewModel);
        var scheduler = new SynchronizationContextWindowUpdateScheduler(
            new DispatcherSynchronizationContext(Dispatcher));
        var windowTracker = new CodexWindowTracker(new WindowNativeApi(), scheduler);
        var resolver = new CodexExecutableResolver(new SystemCodexDiscoveryProbe());
        var connectionFactory = new JsonRpcRateLimitConnectionFactory(
            diagnostic => _ = WriteDiagnosticAsync(log, diagnostic));
        var rateSource = new RateLimitSource(
            new RateLimitSourceDependencies(
                resolver,
                connectionFactory,
                new PassiveAvailabilitySignal(),
                TimeProvider.System,
                GetClientVersion()));
        var executablePath = Environment.ProcessPath ??
            Process.GetCurrentProcess().MainModule?.FileName ??
            Path.Combine(AppContext.BaseDirectory, "CodexQuotaRail.App.exe");
        var autostart = new AutostartService(
            new CurrentUserRunRegistry(),
            executablePath);
        var actions = new DesktopApplicationActions(
            JsonLineLog.GetDefaultDirectory(),
            RequestExit);
        return new ApplicationHost(
            settingsStore,
            new TrayIconFactory(),
            windowTracker,
            rateSource,
            overlay,
            autostart,
            actions,
            new WpfUiDispatcher(Dispatcher),
            log);
    }

    private static Version GetClientVersion() =>
        typeof(App).Assembly.GetName().Version ?? new Version(0, 1, 0);

    private static async Task WriteDiagnosticAsync(
        JsonLineLog log,
        ProcessDiagnostic diagnostic)
    {
        try
        {
            await log.WriteAsync(
                "warning",
                diagnostic.EventName,
                $"App Server 输出了 {diagnostic.CharacterCount} 个诊断字符。",
                cancellationToken: CancellationToken.None);
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
