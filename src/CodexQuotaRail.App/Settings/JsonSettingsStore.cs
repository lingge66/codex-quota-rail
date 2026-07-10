using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexQuotaRail.App.Settings;

public interface IAppSettingsStore : IDisposable
{
    ValueTask<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    ValueTask SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default);
}

public sealed class JsonSettingsStore : IAppSettingsStore
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;

    public JsonSettingsStore(string? path = null)
    {
        var selectedPath = path ?? GetDefaultPath();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            throw new ArgumentException("设置文件路径不能为空。", nameof(path));
        }

        _path = System.IO.Path.GetFullPath(selectedPath);
    }

    public string Path => _path;

    public async ValueTask<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            try
            {
                await using var stream = new FileStream(
                    _path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (document.RootElement.TryGetProperty("schemaVersion", out var schema) &&
                    schema.TryGetInt32(out var schemaVersion) &&
                    schemaVersion > CurrentSchemaVersion)
                {
                    throw new UnsupportedSettingsSchemaException(schemaVersion);
                }

                var settings = document.RootElement.Deserialize<AppSettings>(SerializerOptions);
                if (settings is null)
                {
                    throw new JsonException("设置文件内容为空。");
                }

                return settings with { SchemaVersion = CurrentSchemaVersion };
            }
            catch (UnsupportedSettingsSchemaException)
            {
                throw;
            }
            catch (JsonException)
            {
                PreserveBadFile();
                return new AppSettings();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var temporaryPath = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            var current = settings with { SchemaVersion = CurrentSchemaVersion };
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    current,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            _gate.Release();
        }
    }

    public static string GetDefaultPath() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexQuotaRail",
        "settings.json");

    public void Dispose() => _gate.Dispose();

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private void PreserveBadFile()
    {
        var badPath = _path + ".bad";
        var suffix = 1;
        while (File.Exists(badPath))
        {
            badPath = $"{_path}.bad.{suffix++}";
        }

        File.Move(_path, badPath);
    }
}
