using System.Text.Json;
using CodexQuotaRail.App.Settings;

namespace CodexQuotaRail.App.Tests;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"CodexQuotaRail.Settings.{Guid.NewGuid():N}");

    [Fact]
    public void AppSettingsDefaultsMatchFirstRunRecommendation()
    {
        var settings = new AppSettings();

        Assert.Equal(1, settings.SchemaVersion);
        Assert.True(settings.StartWithWindows);
        Assert.False(settings.ReduceMotion);
        Assert.Equal(ThemePreference.Automatic, settings.Theme);
        Assert.False(settings.FollowPaused);
    }

    [Fact]
    public async Task SaveAsyncAtomicallyReplacesExistingSettings()
    {
        var path = SettingsPath();
        var store = new JsonSettingsStore(path);
        await store.SaveAsync(new AppSettings(ReduceMotion: true));
        await store.SaveAsync(
            new AppSettings(
                StartWithWindows: false,
                Theme: ThemePreference.Light));

        var loaded = await store.LoadAsync();

        Assert.False(loaded.StartWithWindows);
        Assert.False(loaded.ReduceMotion);
        Assert.Equal(ThemePreference.Light, loaded.Theme);
        Assert.False(File.Exists(path + ".tmp"));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public async Task LoadAsyncCorruptJsonFallsBackAndPreservesBadFile()
    {
        var path = SettingsPath();
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(path, "{ definitely-not-json");
        var store = new JsonSettingsStore(path);

        var loaded = await store.LoadAsync();

        Assert.Equal(new AppSettings(), loaded);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(path + ".bad"));
        Assert.Equal("{ definitely-not-json", await File.ReadAllTextAsync(path + ".bad"));
    }

    [Fact]
    public async Task LoadAsyncFutureSchemaThrowsAndDoesNotMoveFile()
    {
        var path = SettingsPath();
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            path,
            "{\"schemaVersion\":99,\"theme\":\"future-neon\"}");
        var store = new JsonSettingsStore(path);

        var error = await Assert.ThrowsAsync<UnsupportedSettingsSchemaException>(
            () => store.LoadAsync().AsTask());

        Assert.Equal(99, error.SchemaVersion);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".bad"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string SettingsPath() => Path.Combine(_directory, "settings.json");
}
