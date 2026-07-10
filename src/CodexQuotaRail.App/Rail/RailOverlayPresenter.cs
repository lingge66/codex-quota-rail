using System.Windows;
using CodexQuotaRail.App.Hosting;
using CodexQuotaRail.App.Settings;
using CodexQuotaRail.Core.Quotas;
using CodexQuotaRail.Windows.Overlay;
using Microsoft.Win32;

namespace CodexQuotaRail.App.Rail;

public sealed class RailOverlayPresenter : IOverlayPresenter
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private readonly RailQuotaRenderer _renderer;
    private readonly RailViewModel _viewModel;
    private readonly RailWindow _window;
    private int _disposed;

    public RailOverlayPresenter(
        RailWindow window,
        RailQuotaRenderer renderer,
        RailViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(viewModel);
        _window = window;
        _renderer = renderer;
        _viewModel = viewModel;
    }

    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _viewModel.ReduceMotion = settings.ReduceMotion;
        ApplyTheme(settings.Theme);
    }

    public void Present(
        QuotaDisplayState state,
        OverlayPlacement placement,
        double dpiScale)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(placement);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _renderer.Render(state, placement.Mode);
        _window.QueuePlacement(placement, dpiScale);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _window.Close();
    }

    private static void ApplyTheme(ThemePreference preference)
    {
        var application = System.Windows.Application.Current;
        if (application is null)
        {
            return;
        }

        var selected = preference == ThemePreference.Automatic
            ? ReadAutomaticTheme()
            : preference;
        var fileName = selected == ThemePreference.Light
            ? "Theme.Light.xaml"
            : "Theme.Dark.xaml";
        var dictionaries = application.Resources.MergedDictionaries;
        var current = dictionaries.FirstOrDefault(
            dictionary => dictionary.Source?.OriginalString.Contains(
                "Theme.",
                StringComparison.OrdinalIgnoreCase) is true);
        var source = new Uri($"Resources/{fileName}", UriKind.Relative);
        if (current is not null && current.Source == source)
        {
            return;
        }

        var replacement = new ResourceDictionary { Source = source };
        if (current is null)
        {
            dictionaries.Add(replacement);
            return;
        }

        dictionaries[dictionaries.IndexOf(current)] = replacement;
    }

    private static ThemePreference ReadAutomaticTheme()
    {
        if (SystemParameters.HighContrast)
        {
            return ThemePreference.Dark;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0
                ? ThemePreference.Light
                : ThemePreference.Dark;
        }
        catch (System.Security.SecurityException)
        {
            return ThemePreference.Dark;
        }
        catch (UnauthorizedAccessException)
        {
            return ThemePreference.Dark;
        }
    }
}
