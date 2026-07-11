using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexQuotaRail.App.Rail;
using CodexQuotaRail.App.Settings;

namespace CodexQuotaRail.App.Tests;

public sealed partial class AccessibilityStateTests
{
    [Fact]
    public async Task ExistingRailUpdatesItsThemeBrushesImmediately()
    {
        var colors = await _wpf.InvokeAsync(
            () =>
            {
                var application = System.Windows.Application.Current!;
                var dictionaries = application.Resources.MergedDictionaries;
                foreach (var theme in dictionaries
                             .Where(dictionary => dictionary.Source?.OriginalString.Contains(
                                 "Theme.",
                                 StringComparison.OrdinalIgnoreCase) is true)
                             .ToArray())
                {
                    dictionaries.Remove(theme);
                }

                if (!dictionaries.Any(dictionary => dictionary.Source?.OriginalString.Contains(
                        "Design.Tokens.xaml",
                        StringComparison.OrdinalIgnoreCase) is true))
                {
                    dictionaries.Add(
                        new ResourceDictionary
                        {
                            Source = new Uri(
                                "/CodexQuotaRail.App;component/Resources/Design.Tokens.xaml",
                                UriKind.Relative),
                        });
                }

                dictionaries.Add(
                    new ResourceDictionary
                    {
                        Source = new Uri(
                            "/CodexQuotaRail.App;component/Resources/Theme.Dark.xaml",
                            UriKind.Relative),
                    });
                var viewModel = new RailViewModel();
                var window = new RailWindow(viewModel);
                var renderer = new RailQuotaRenderer(viewModel, window.Dispatcher);
                using var presenter = new RailOverlayPresenter(window, renderer, viewModel);
                var root = (Border)window.FindName("RootBorder");
                var dark = Assert.IsType<SolidColorBrush>(root.Background).Color;

                presenter.ApplySettings(new AppSettings(Theme: ThemePreference.Light));

                var light = Assert.IsType<SolidColorBrush>(root.Background).Color;
                return (Dark: dark, Light: light);
            });

        Assert.Equal(Color.FromArgb(0xF2, 0x10, 0x10, 0x0E), colors.Dark);
        Assert.Equal(Color.FromArgb(0xF2, 0xF8, 0xF8, 0xF6), colors.Light);
    }
}
