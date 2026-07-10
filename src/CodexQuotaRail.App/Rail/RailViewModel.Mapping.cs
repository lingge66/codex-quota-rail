using CodexQuotaRail.Core.Quotas;

namespace CodexQuotaRail.App.Rail;

public sealed partial class RailViewModel
{
    private static readonly RgbColor UnlimitedColor = new(145, 239, 107);
    private static readonly RgbColor UnavailableColor = new(133, 135, 128);

    private bool CrossedWarningThreshold(QuotaTrackViewModel track)
    {
        if (track.AvailablePercent is not int current ||
            !_previousAvailable.TryGetValue(track.Label, out var previousValue) ||
            previousValue is not int previous)
        {
            return false;
        }

        return (previous > 50 && current <= 50) ||
               (previous > 20 && current <= 20);
    }

    private QuotaTrackViewModel CreateTrack(QuotaWindowDisplay window)
    {
        int? available = window.AvailablePercent is int value
            ? Math.Clamp(value, 0, 100)
            : null;
        var unlimited = window.State == QuotaWindowState.Unlimited;
        var color = unlimited
            ? UnlimitedColor
            : available.HasValue
                ? QuotaColorScale.ForAvailable(available.Value)
                : UnavailableColor;
        var valueText = unlimited
            ? "无限"
            : available.HasValue
                ? $"可用 {available.Value}%"
                : "暂不可用";
        return new QuotaTrackViewModel(
            window.Label,
            available,
            available.GetValueOrDefault() / 100.0,
            color,
            FormatReset(window.ResetsAt),
            window.State,
            unlimited,
            valueText);
    }

    private string FormatReset(DateTimeOffset? resetsAt)
    {
        if (resetsAt is null)
        {
            return "重置时间未知";
        }

        var remaining = resetsAt.Value - _timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            return "即将重置";
        }

        if (remaining < TimeSpan.FromHours(1))
        {
            return $"约 {Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))} 分钟后";
        }

        if (remaining < TimeSpan.FromDays(1))
        {
            return $"约 {Math.Max(1, (int)Math.Ceiling(remaining.TotalHours))} 小时后";
        }

        return $"约 {Math.Max(1, (int)Math.Ceiling(remaining.TotalDays))} 天后";
    }

    private static string CreateStatusText(QuotaDisplayState state) =>
        state.Connection switch
        {
            QuotaConnectionState.Connecting => state.Message ?? "正在连接 Codex",
            QuotaConnectionState.Live when state.Windows.Count > 0 => "额度已更新",
            QuotaConnectionState.AuthenticationRequired => "请先在 Codex 中登录",
            QuotaConnectionState.Unsupported => "未发现可用 Codex CLI",
            QuotaConnectionState.Stale => "额度数据可能已过期",
            _ => "额度暂不可用",
        };
}
