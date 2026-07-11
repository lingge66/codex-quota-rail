using System.Windows;

namespace CodexQuotaRail.App.Hosting;

public sealed class WpfAccessibilitySettings : IAccessibilitySettings
{
    public bool ClientAreaAnimationEnabled => SystemParameters.ClientAreaAnimation;
}
