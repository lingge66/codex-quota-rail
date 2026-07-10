using CodexQuotaRail.Windows.Startup;

namespace CodexQuotaRail.Windows.Tests;

public sealed class AutostartServiceTests
{
    [Fact]
    public void SetEnabledWritesOnlyExactQuotedExecutableAndBackgroundSwitch()
    {
        var registry = new FakeRunRegistry();
        var service = new AutostartService(
            registry,
            @"C:\Program Files\CodexQuotaRail\CodexQuotaRail.exe");

        service.SetEnabled(true);

        Assert.Equal(
            "\"C:\\Program Files\\CodexQuotaRail\\CodexQuotaRail.exe\" --background",
            registry.Values[AutostartService.ValueName]);
        Assert.True(service.IsEnabled());
    }

    [Fact]
    public void SetEnabledFalseDeletesValueAndMismatchedValueIsNotEnabled()
    {
        var registry = new FakeRunRegistry();
        registry.Values[AutostartService.ValueName] = "malicious.exe";
        var service = new AutostartService(registry, @"C:\Apps\CodexQuotaRail.exe");

        Assert.False(service.IsEnabled());

        service.SetEnabled(false);

        Assert.DoesNotContain(AutostartService.ValueName, registry.Values.Keys);
    }

    private sealed class FakeRunRegistry : IRunRegistry
    {
        public Dictionary<string, string> Values { get; } = [];

        public void DeleteValue(string name) => Values.Remove(name);

        public string? GetValue(string name) => Values.GetValueOrDefault(name);

        public void SetValue(string name, string value) => Values[name] = value;
    }
}
