using System.Windows.Threading;
using CodexQuotaRail.Core.Quotas;
using CodexQuotaRail.Core.Rendering;
using CodexQuotaRail.Windows.Overlay;

namespace CodexQuotaRail.App.Rail;

public sealed class RailQuotaRenderer : IQuotaRenderer
{
    private readonly Dispatcher _dispatcher;
    private readonly RailViewModel _viewModel;
    private OverlayMode _mode = OverlayMode.ExternalRail;

    public RailQuotaRenderer(RailViewModel viewModel, Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _viewModel = viewModel;
        _dispatcher = dispatcher;
    }

    public void Render(QuotaDisplayState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ApplyOnDispatcher(state, _mode);
    }

    public void Render(QuotaDisplayState state, OverlayMode mode)
    {
        ArgumentNullException.ThrowIfNull(state);
        _mode = mode;
        ApplyOnDispatcher(state, mode);
    }

    public void SetMode(OverlayMode mode) => _mode = mode;

    private void ApplyOnDispatcher(QuotaDisplayState state, OverlayMode mode)
    {
        if (_dispatcher.CheckAccess())
        {
            _viewModel.Apply(state, mode);
            return;
        }

        _ = _dispatcher.BeginInvoke(
            () => _viewModel.Apply(state, mode),
            DispatcherPriority.DataBind);
    }
}
