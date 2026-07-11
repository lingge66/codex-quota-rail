using CodexQuotaRail.App.Settings;
using CodexQuotaRail.App.Tray;
using CodexQuotaRail.Core.Quotas;
using CodexQuotaRail.Windows.Windows;

namespace CodexQuotaRail.App.Hosting;

public sealed partial class ApplicationHost
{
    private void OnRateSnapshotChanged(object? sender, RawQuotaSnapshot snapshot) =>
        QueueEvent("rate_snapshot", token => ApplySnapshotAsync(snapshot, token));

    private void OnConnectionChanged(object? sender, QuotaConnectionState state) =>
        QueueEvent("connection_changed", token => ApplyConnectionAsync(state, token));

    private void OnWindowSnapshotChanged(object? sender, TrackedWindowSnapshot? snapshot) =>
        QueueEvent("window_changed", token => ApplyWindowAsync(snapshot, token));

    private void OnTrayCommandRequested(object? sender, TrayCommandRequest request) =>
        QueueEvent("tray_command", token => HandleTrayCommandAsync(request, token));

    private void OnTaskbarRestarted(object? sender, EventArgs eventArgs) =>
        QueueEvent(
            "taskbar_restarted",
            token => _dispatcher.InvokeAsync(
                    () => _tray?.RecreateIcon(),
                    token)
                .AsTask());

    private void QueueEvent(
        string eventName,
        Func<CancellationToken, Task> callback)
    {
        lock (_sync)
        {
            if (!_eventsEnabled)
            {
                return;
            }

            _eventTail = RunEventAsync(_eventTail, eventName, callback);
        }
    }

    private async Task RunEventAsync(
        Task previous,
        string eventName,
        Func<CancellationToken, Task> callback)
    {
        try
        {
            await previous.ConfigureAwait(false);
            await callback(_lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            await SafeLogAsync(eventName, error).ConfigureAwait(false);
        }
    }

    private async Task ApplySnapshotAsync(
        RawQuotaSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var normalized = QuotaNormalizer.Normalize(snapshot);
        await _dispatcher.InvokeAsync(
            () =>
            {
                _display = normalized;
                RenderCurrent();
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyConnectionAsync(
        QuotaConnectionState state,
        CancellationToken cancellationToken)
    {
        await _dispatcher.InvokeAsync(
            () =>
            {
                _display = _display with
                {
                    Connection = state,
                    Message = ConnectionMessage(state),
                };
                RenderCurrent();
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyWindowAsync(
        TrackedWindowSnapshot? snapshot,
        CancellationToken cancellationToken)
    {
        if (_settings.FollowPaused)
        {
            return;
        }

        await _dispatcher.InvokeAsync(
            () =>
            {
                _window = snapshot;
                RenderCurrent();
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleTrayCommandAsync(
        TrayCommandRequest request,
        CancellationToken cancellationToken)
    {
        switch (request.Command)
        {
            case TrayCommand.Refresh:
                await _rateSource.RefreshAsync(cancellationToken).ConfigureAwait(false);
                return;
            case TrayCommand.SetFollowPaused:
                await SaveSettingsAsync(
                    _settings with { FollowPaused = request.BooleanValue is true },
                    cancellationToken).ConfigureAwait(false);
                return;
            case TrayCommand.SetTheme:
                await SaveSettingsAsync(
                    _settings with { Theme = request.Theme ?? ThemePreference.Automatic },
                    cancellationToken).ConfigureAwait(false);
                return;
            case TrayCommand.SetReduceMotion:
                await SaveSettingsAsync(
                    _settings with
                    {
                        ReduceMotion = request.BooleanValue is true,
                        ReduceMotionConfigured = true,
                    },
                    cancellationToken).ConfigureAwait(false);
                return;
            case TrayCommand.SetAutostart:
                var enabled = request.BooleanValue is true;
                _autostart.SetEnabled(enabled);
                await SaveSettingsAsync(
                    _settings with { StartWithWindows = enabled },
                    cancellationToken).ConfigureAwait(false);
                return;
            default:
                await InvokeActionAsync(request.Command, cancellationToken).ConfigureAwait(false);
                return;
        }
    }

    private async Task SaveSettingsAsync(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        await _settingsStore.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        await _dispatcher.InvokeAsync(
            () =>
            {
                _settings = settings;
                if (!settings.FollowPaused)
                {
                    _window = _windowTracker.CurrentSnapshot;
                }

                _overlay.ApplySettings(settings);
                RenderCurrent();
            },
            cancellationToken).ConfigureAwait(false);
    }

    private ValueTask InvokeActionAsync(
        TrayCommand command,
        CancellationToken cancellationToken) => _dispatcher.InvokeAsync(
        () =>
        {
            switch (command)
            {
                case TrayCommand.CheckUpdates:
                    _actions.CheckForUpdates();
                    break;
                case TrayCommand.OpenLogs:
                    _actions.OpenLogs();
                    break;
                case TrayCommand.Troubleshoot:
                    _actions.ShowTroubleshooting();
                    break;
                case TrayCommand.Exit:
                    _actions.RequestExit();
                    break;
            }
        },
        cancellationToken);

    private async ValueTask SafeLogAsync(string eventName, Exception error)
    {
        try
        {
            await _log.WriteAsync(
                "error",
                eventName,
                "应用事件处理失败。",
                error,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static string? ConnectionMessage(QuotaConnectionState state) => state switch
    {
        QuotaConnectionState.Connecting => "正在连接 Codex",
        QuotaConnectionState.Stale => "连接暂时中断，显示最近额度",
        QuotaConnectionState.AuthenticationRequired => "请先在 Codex 中登录",
        QuotaConnectionState.Unsupported => "当前 Codex 版本暂不支持额度读取",
        QuotaConnectionState.Unavailable => "Codex 暂不可用",
        _ => null,
    };
}
