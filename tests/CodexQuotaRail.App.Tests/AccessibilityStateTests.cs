using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using CodexQuotaRail.App.Rail;
using CodexQuotaRail.App.Settings;

namespace CodexQuotaRail.App.Tests;

[Collection(WpfTestGroup.Name)]
public sealed partial class AccessibilityStateTests
{
    private readonly WpfTestHost _wpf;

    public AccessibilityStateTests(WpfTestHost wpf)
    {
        _wpf = wpf;
    }

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
        var state = await _wpf.InvokeAsync(
            () =>
            {
                var application = System.Windows.Application.Current!;
                foreach (var dictionary in application.Resources.MergedDictionaries.ToArray())
                {
                    application.Resources.MergedDictionaries.Remove(dictionary);
                }

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
                var ownerWindow = new Window
                {
                    Left = -10_000,
                    Top = -10_000,
                    Width = 800,
                    Height = 600,
                    ShowInTaskbar = false,
                };
                try
                {
                    var root = (FrameworkElement)window.FindName("RootBorder");
                    var popup = (Popup)window.FindName("DetailsPopup");
                    var startsTopmost = window.Topmost;
                    var popupChild = (FrameworkElement)popup.Child;
                    popupChild.LayoutTransform = new System.Windows.Media.ScaleTransform(2, 2);
                    popupChild.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    window.Left = -10_000;
                    window.Top = -10_000;
                    ownerWindow.Show();
                    var ownerHandle = new WindowInteropHelper(ownerWindow).Handle;
                    window.Show();
                    var railHandle = new WindowInteropHelper(window).Handle;
                    var mouseActivateResult = SendMessage(railHandle, 0x0021, 0, 0);
                    window.QueuePlacement(
                        new CodexQuotaRail.Windows.Overlay.OverlayPlacement(
                            new CodexQuotaRail.Windows.Windows.PixelRect(-10_000, -10_000, 800, 22),
                            CodexQuotaRail.Windows.Overlay.OverlayMode.ExternalRail,
                            1),
                        dpiScale: 1,
                        ownerHandle: ownerHandle);
                    var mousePosition = new nint((11 << 16) | 400);
                    SendMessage(railHandle, 0x0201, 1, mousePosition);
                    SendMessage(railHandle, 0x0202, 0, mousePosition);
                    var nativeClickOpenedDetails = popup.IsOpen;
                    popup.IsOpen = true;
                    window.QueuePlacement(
                        new CodexQuotaRail.Windows.Overlay.OverlayPlacement(
                            new CodexQuotaRail.Windows.Windows.PixelRect(-10_000, -10_000, 1920, 4),
                            CodexQuotaRail.Windows.Overlay.OverlayMode.CompactTitleBar,
                            1),
                        dpiScale: 1,
                        ownerHandle: ownerHandle);
                    var compactPlacementClosedDetails = !popup.IsOpen;
                    popup.IsOpen = true;
                    window.QueuePlacement(
                        new CodexQuotaRail.Windows.Overlay.OverlayPlacement(
                            new CodexQuotaRail.Windows.Windows.PixelRect(100, 78, 800, 22),
                            CodexQuotaRail.Windows.Overlay.OverlayMode.ExternalRail,
                            0.52),
                        dpiScale: 1,
                        ownerHandle: ownerHandle);
                    var detailsClosedWhenUnfocused = !popup.IsOpen;
                    SendMessage(railHandle, 0x0201, 1, mousePosition);
                    SendMessage(railHandle, 0x0202, 0, mousePosition);
                    var unfocusedRailClickOpenedDetails = popup.IsOpen;
                    window.QueuePlacement(
                        new CodexQuotaRail.Windows.Overlay.OverlayPlacement(
                            new CodexQuotaRail.Windows.Windows.PixelRect(-10_000, -10_000, 1920, 4),
                            CodexQuotaRail.Windows.Overlay.OverlayMode.CompactTitleBar,
                            1),
                        dpiScale: 1,
                        ownerHandle: ownerHandle);
                    return new RailAccessibilityResult(
                        window.ShowActivated,
                        window.ShowInTaskbar,
                        startsTopmost,
                        window.Focusable,
                        AutomationProperties.GetName(root),
                        popup.Focusable,
                        popup.StaysOpen,
                        popupChild.DesiredSize.Height,
                        MouseClickDoesNotActivate: mouseActivateResult == 3,
                        nativeClickOpenedDetails,
                        unfocusedRailClickOpenedDetails,
                        compactPlacementClosedDetails,
                        detailsClosedWhenUnfocused,
                        RailOwnsTrackedWindow: GetWindow(railHandle, 4) == ownerHandle,
                        RailIsNotGloballyTopmost: !window.Topmost);
                }
                finally
                {
                    window.Close();
                    ownerWindow.Close();
                }
            });

        Assert.False(state.ShowActivated);
        Assert.False(state.ShowInTaskbar);
        Assert.False(state.Topmost);
        Assert.False(state.Focusable);
        Assert.Equal("Codex 可用额度", state.AutomationName);
        Assert.False(state.PopupFocusable);
        Assert.True(state.PopupUsesExplicitClosePolicy);
        Assert.True(state.ScaledDetailHeight > 60);
        Assert.True(state.MouseClickDoesNotActivate);
        Assert.True(state.NativeClickOpenedDetails);
        Assert.True(state.UnfocusedRailClickOpenedDetails);
        Assert.True(state.CompactPlacementClosedDetails);
        Assert.True(state.DetailsClosedWhenUnfocused);
        Assert.True(state.RailOwnsTrackedWindow);
        Assert.True(state.RailIsNotGloballyTopmost);
    }

    private sealed record RailAccessibilityResult(
        bool ShowActivated,
        bool ShowInTaskbar,
        bool Topmost,
        bool Focusable,
        string AutomationName,
        bool PopupFocusable,
        bool PopupUsesExplicitClosePolicy,
        double ScaledDetailHeight,
        bool MouseClickDoesNotActivate,
        bool NativeClickOpenedDetails,
        bool UnfocusedRailClickOpenedDetails,
        bool CompactPlacementClosedDetails,
        bool DetailsClosedWhenUnfocused,
        bool RailOwnsTrackedWindow,
        bool RailIsNotGloballyTopmost);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint handle, uint command);

    [DllImport("user32.dll")]
    private static extern nint SendMessage(
        nint handle,
        uint message,
        nint wordParameter,
        nint longParameter);
}
