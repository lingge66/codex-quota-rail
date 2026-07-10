namespace CodexQuotaRail.App.Settings;

public enum ThemePreference
{
    Automatic,
    Dark,
    Light,
}

public sealed record AppSettings(
    int SchemaVersion = 1,
    bool StartWithWindows = true,
    bool ReduceMotion = false,
    ThemePreference Theme = ThemePreference.Automatic,
    bool FollowPaused = false);

public sealed class UnsupportedSettingsSchemaException(int schemaVersion)
    : Exception($"设置文件版本 {schemaVersion} 高于当前程序支持的版本 1。")
{
    public int SchemaVersion { get; } = schemaVersion;
}
