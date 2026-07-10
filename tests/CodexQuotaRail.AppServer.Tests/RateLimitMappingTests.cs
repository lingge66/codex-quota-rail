using System.Text.Json;
using CodexQuotaRail.AppServer.Protocol;
using CodexQuotaRail.Core.Quotas;

namespace CodexQuotaRail.AppServer.Tests;

public sealed class RateLimitMappingTests
{
    [Fact]
    public void MapRetainsBothWindowsAndConvertsUsedToAvailable()
    {
        // Given
        using var document = JsonDocument.Parse(
            """
            {
              "rateLimits": {
                "primary": {
                  "usedPercent": 68,
                  "windowDurationMins": 300,
                  "resetsAt": 1893456000
                },
                "secondary": {
                  "usedPercent": 25,
                  "windowDurationMins": 10080,
                  "resetsAt": 1894060800
                },
                "credits": { "unlimited": false },
                "planType": "plus"
              },
              "rateLimitsByLimitId": {
                "codex": { "primary": { "usedPercent": 1 } }
              }
            }
            """);

        // When
        var raw = RateLimitSnapshotMapper.Map(
            document.RootElement,
            DateTimeOffset.UnixEpoch);
        var display = QuotaNormalizer.Normalize(raw);

        // Then
        Assert.Equal(68, raw.Primary?.UsedPercent);
        Assert.Equal(25, raw.Secondary?.UsedPercent);
        Assert.Equal("5 小时", raw.Primary?.Label);
        Assert.Equal("本周", raw.Secondary?.Label);
        Assert.Equal(32, display.Windows[0].AvailablePercent);
        Assert.Equal(75, display.Windows[1].AvailablePercent);
    }

    [Fact]
    public void MapKeepsMissingValuesUnavailableAndHonorsUnlimited()
    {
        // Given
        using var missingDocument = JsonDocument.Parse(
            """
            {
              "rateLimits": {
                "primary": { "windowDurationMins": 300 },
                "credits": { "unlimited": false }
              }
            }
            """);
        using var unlimitedDocument = JsonDocument.Parse(
            """
            {
              "rateLimits": {
                "primary": { "usedPercent": 99 },
                "credits": { "unlimited": true }
              }
            }
            """);

        // When
        var missing = RateLimitSnapshotMapper.Map(
            missingDocument.RootElement,
            DateTimeOffset.UnixEpoch);
        var unlimited = RateLimitSnapshotMapper.Map(
            unlimitedDocument.RootElement,
            DateTimeOffset.UnixEpoch);

        // Then
        Assert.Null(missing.Primary?.UsedPercent);
        Assert.Null(missing.Secondary);
        Assert.True(unlimited.Primary?.IsUnlimited);
    }

    [Fact]
    public void MapDowngradesInvalidResetValuesWithoutThrowing()
    {
        // Given
        using var document = JsonDocument.Parse(
            """
            {
              "rateLimits": {
                "primary": {
                  "usedPercent": 10,
                  "windowDurationMins": 300,
                  "resetsAt": 999999999999999999999999999999
                }
              }
            }
            """);

        // When
        var snapshot = RateLimitSnapshotMapper.Map(
            document.RootElement,
            DateTimeOffset.UnixEpoch);
        var display = QuotaNormalizer.Normalize(snapshot);

        // Then
        Assert.Null(snapshot.Primary?.ResetsAtUnixSeconds);
        Assert.Null(display.Windows[0].ResetsAt);
    }

    [Fact]
    public void MapRejectsMissingPrimaryInsteadOfPublishingFalseLiveData()
    {
        // Given
        using var document = JsonDocument.Parse(
            """
            {
              "rateLimits": {
                "secondary": { "usedPercent": 20 }
              }
            }
            """);

        // When
        var error = Record.Exception(
            () => RateLimitSnapshotMapper.Map(
                document.RootElement,
                DateTimeOffset.UnixEpoch));

        // Then
        Assert.IsType<AppServerProtocolException>(error);
        Assert.DoesNotContain("secondary", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
