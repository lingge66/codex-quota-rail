using Microsoft.Win32;

namespace CodexQuotaRail.Windows.Startup;

public interface IRunRegistry
{
    string? GetValue(string name);

    void SetValue(string name, string value);

    void DeleteValue(string name);
}

public interface IAutostartService
{
    bool IsEnabled();

    void SetEnabled(bool enabled);
}

public sealed class AutostartService : IAutostartService
{
    public const string ValueName = "CodexQuotaRail";
    private readonly string _command;
    private readonly IRunRegistry _registry;

    public AutostartService(IRunRegistry registry, string executablePath)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!Path.IsPathFullyQualified(executablePath))
        {
            throw new ArgumentException("自启程序必须使用绝对路径。", nameof(executablePath));
        }

        _registry = registry;
        _command = $"\"{Path.GetFullPath(executablePath)}\" --background";
    }

    public bool IsEnabled() => string.Equals(
        _registry.GetValue(ValueName),
        _command,
        StringComparison.Ordinal);

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            _registry.SetValue(ValueName, _command);
            return;
        }

        _registry.DeleteValue(ValueName);
    }
}

public sealed class CurrentUserRunRegistry : IRunRegistry
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? GetValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
            as string;
    }

    public void SetValue(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void DeleteValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
