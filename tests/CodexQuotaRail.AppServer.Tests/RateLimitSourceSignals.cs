using System.Text.Json;
using CodexQuotaRail.AppServer.Discovery;
using CodexQuotaRail.AppServer.RateLimits;

namespace CodexQuotaRail.AppServer.Tests;

internal sealed class FakeRateLimitAvailabilitySignal : IRateLimitAvailabilitySignal
{
    public event EventHandler? Paused;

    public event EventHandler? Resumed;

    public event EventHandler? NetworkAvailable;

    public bool IsPaused { get; private set; }

    public void Pause()
    {
        IsPaused = true;
        Paused?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        IsPaused = false;
        Resumed?.Invoke(this, EventArgs.Empty);
    }

    public void SignalNetworkAvailable() => NetworkAvailable?.Invoke(this, EventArgs.Empty);
}

internal sealed class FakeSourceDiscoveryProbe(string executablePath) : ICodexDiscoveryProbe
{
    public IReadOnlyList<string> InstalledPackageExecutablePaths => [];

    public IReadOnlyList<string> RunningExecutablePaths => [];

    public string? GetEnvironmentVariable(string name) =>
        name == "CODEX_QUOTA_RAIL_CODEX_PATH" ? executablePath : null;

    public bool IsExecutableFile(string path) => path == executablePath;
}

internal static class JsonFixture
{
    public static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static JsonElement RateLimits(int usedPercent) =>
        Element(
            $$"""
            {
              "rateLimits": {
                "primary": {
                  "usedPercent": {{usedPercent}},
                  "windowDurationMins": 300,
                  "resetsAt": 1893456000
                },
                "credits": { "unlimited": false },
                "planType": "plus"
              }
            }
            """);
}
