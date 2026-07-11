using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using CodexQuotaRail.App.Rail;
using CodexQuotaRail.App.Settings;

namespace CodexQuotaRail.App.Tests;

public sealed class AccessibilityStateTests
{
    [Fact]
    public async Task SystemAnimationSettingBecomesDefaultUntilUserChooses()
    {
        await using var fixture = new ApplicationHostFixture();
        fixture.Accessibility.ClientAreaAnimationEnabled = false;

        await fixture.Host.StartAsync(CancellationToken.None);

        Assert.True(fixture.Overlay.AppliedSettings[0].ReduceMotion);
        Assert.True(fixture.TrayFactory.Created!.States[^1].ReduceMotion);
        Assert.Empty(fixture.SettingsStore.Saved);
    }

    [Fact]
    public async Task ExplicitAnimationPreferenceOverridesWindowsSetting()
    {
        await using var fixture = new ApplicationHostFixture();
        fixture.Accessibility.ClientAreaAnimationEnabled = false;
        fixture.SettingsStore.Initial = new AppSettings(
            ReduceMotion: false,
            ReduceMotionConfigured: true);

        await fixture.Host.StartAsync(CancellationToken.None);

        Assert.False(fixture.Overlay.AppliedSettings[0].ReduceMotion);
    }

    [Theory]
    [InlineData(false, 180)]
    [InlineData(true, 0)]
    public void FocusOpacityMotionHonorsReduceMotion(bool reduceMotion, int milliseconds)
    {
        Assert.Equal(
            TimeSpan.FromMilliseconds(milliseconds),
            RailMotion.FocusOpacityDuration(reduceMotion));
    }

    [Theory]
    [InlineData(1.0, 1.0, false)]
    [InlineData(1.0, 1.0005, false)]
    [InlineData(1.0, 1.5, true)]
    public void DpiReapplyIgnoresDuplicateScale(
        double currentScale,
        double nextScale,
        bool expected)
    {
        Assert.Equal(expected, RailMotion.ShouldReapplyForDpi(currentScale, nextScale));
    }

    [Fact]
    public async Task RailIsNonActivatingNamedAndDetailsRemainUnclippedAtTwoHundredPercent()
    {
        var result = new TaskCompletionSource<RailAccessibilityResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(
            () =>
            {
                try
                {
                    var application = System.Windows.Application.Current ??
                        new System.Windows.Application();
                    application.Resources.MergedDictionaries.Add(
                        new ResourceDictionary
                        {
                            Source = new Uri(
                                "/CodexQuotaRail.App;component/Resources/Design.Tokens.xaml",
                                UriKind.Relative),
                        });
                    application.Resources.MergedDictionaries.Add(
                        new ResourceDictionary
                        {
                            Source = new Uri(
                                "/CodexQuotaRail.App;component/Resources/Theme.Dark.xaml",
                                UriKind.Relative),
                        });
                    var window = new RailWindow();
                    var root = (FrameworkElement)window.FindName("RootBorder");
                    var popup = (Popup)window.FindName("DetailsPopup");
                    var popupChild = (FrameworkElement)popup.Child;
                    popupChild.LayoutTransform = new System.Windows.Media.ScaleTransform(2, 2);
                    popupChild.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    result.TrySetResult(
                        new RailAccessibilityResult(
                            window.ShowActivated,
                            window.ShowInTaskbar,
                            window.Topmost,
                            window.Focusable,
                            AutomationProperties.GetName(root),
                            popup.Focusable,
                            popupChild.DesiredSize.Height));
                    window.Close();
                }
                catch (Exception error)
                {
                    result.TrySetException(error);
                }
            });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var state = await result.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(state.ShowActivated);
        Assert.False(state.ShowInTaskbar);
        Assert.False(state.Topmost);
        Assert.False(state.Focusable);
        Assert.Equal("Codex 可用额度", state.AutomationName);
        Assert.False(state.PopupFocusable);
        Assert.True(state.ScaledDetailHeight > 60);
        Assert.True(thread.Join(TimeSpan.FromSeconds(2)));
    }

    private sealed record RailAccessibilityResult(
        bool ShowActivated,
        bool ShowInTaskbar,
        bool Topmost,
        bool Focusable,
        string AutomationName,
        bool PopupFocusable,
        double ScaledDetailHeight);
}
