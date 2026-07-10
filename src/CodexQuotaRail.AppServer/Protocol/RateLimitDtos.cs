using System.Text.Json;
using System.Text.Json.Serialization;
using CodexQuotaRail.Core.Quotas;

namespace CodexQuotaRail.AppServer.Protocol;

public sealed record AccountReadResponse
{
    [JsonPropertyName("account")]
    public JsonElement? Account { get; init; }
}

public sealed record RateLimitsReadResponse
{
    [JsonPropertyName("rateLimits")]
    public RateLimitBucketDto? RateLimits { get; init; }

    [JsonPropertyName("rateLimitsByLimitId")]
    public IReadOnlyDictionary<string, RateLimitBucketDto>? RateLimitsByLimitId { get; init; }
}

public sealed record RateLimitBucketDto
{
    [JsonPropertyName("primary")]
    public RateLimitWindowDto? Primary { get; init; }

    [JsonPropertyName("secondary")]
    public RateLimitWindowDto? Secondary { get; init; }

    [JsonPropertyName("credits")]
    public RateLimitCreditsDto? Credits { get; init; }

    [JsonPropertyName("planType")]
    public string? PlanType { get; init; }
}

public sealed record RateLimitWindowDto
{
    [JsonPropertyName("usedPercent")]
    public int? UsedPercent { get; init; }

    [JsonPropertyName("windowDurationMins")]
    public long? WindowDurationMins { get; init; }

    [JsonPropertyName("resetsAt")]
    public JsonElement? ResetsAt { get; init; }
}

public sealed record RateLimitCreditsDto
{
    [JsonPropertyName("unlimited")]
    public bool? Unlimited { get; init; }
}

public static class RateLimitSnapshotMapper
{
    private const long MaximumUnixSeconds = 253402300799;
    private const long MinimumUnixSeconds = -62135596800;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    public static RawQuotaSnapshot Map(JsonElement result, DateTimeOffset receivedAt)
    {
        RateLimitsReadResponse? response;
        try
        {
            response = result.Deserialize<RateLimitsReadResponse>(SerializerOptions);
        }
        catch (JsonException)
        {
            throw new AppServerProtocolException("额度响应格式无效。");
        }

        var limits = response?.RateLimits ??
            throw new AppServerProtocolException("额度响应缺少必要字段。");
        if (limits.Primary is null)
        {
            throw new AppServerProtocolException("额度响应缺少必要字段。");
        }

        var unlimited = limits.Credits?.Unlimited is true;
        return new RawQuotaSnapshot(
            MapWindow("主额度", limits.Primary, unlimited),
            MapWindow("次额度", limits.Secondary, unlimited),
            receivedAt);
    }

    private static RawQuotaWindow? MapWindow(
        string label,
        RateLimitWindowDto? source,
        bool unlimited)
    {
        if (source is null)
        {
            return null;
        }

        return new RawQuotaWindow(
            DisplayLabel(label, source.WindowDurationMins),
            source.UsedPercent,
            SafeDurationMinutes(source.WindowDurationMins),
            SafeUnixSeconds(source.ResetsAt),
            unlimited);
    }

    private static string DisplayLabel(string fallback, long? durationMinutes) =>
        durationMinutes switch
        {
            300 => "5 小时",
            10_080 => "本周",
            _ => fallback,
        };

    private static long? SafeDurationMinutes(long? minutes)
    {
        if (minutes is null || minutes < 0)
        {
            return null;
        }

        var maximumMinutes = (long)TimeSpan.MaxValue.TotalMinutes;
        return minutes <= maximumMinutes ? minutes : null;
    }

    private static long? SafeUnixSeconds(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Number } value ||
            !value.TryGetInt64(out var seconds))
        {
            return null;
        }

        return seconds is >= MinimumUnixSeconds and <= MaximumUnixSeconds
            ? seconds
            : null;
    }
}
