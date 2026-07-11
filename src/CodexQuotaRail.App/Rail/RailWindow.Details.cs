using System.Windows.Threading;
using CodexQuotaRail.Windows.Overlay;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace CodexQuotaRail.App.Rail;

public partial class RailWindow
{
    private static readonly TimeSpan DetailCloseGrace = TimeSpan.FromMilliseconds(160);
    private readonly DispatcherTimer _closeDetailsTimer;

    private void OnRailMouseEnter(object sender, MouseEventArgs eventArgs)
    {
        _closeDetailsTimer.Stop();
    }

    private void OnRailMouseLeave(object sender, MouseEventArgs eventArgs)
    {
        ScheduleDetailsClose();
    }

    private void ToggleDetails()
    {
        if (_closed ||
            _lastPlacement is not { } placement ||
            !CanOpenDetails(placement))
        {
            CloseDetails();
            return;
        }

        _closeDetailsTimer.Stop();
        DetailsPopup.IsOpen = !DetailsPopup.IsOpen;
    }

    private void OnDetailsMouseEnter(object sender, MouseEventArgs eventArgs) =>
        _closeDetailsTimer.Stop();

    private void OnDetailsMouseLeave(object sender, MouseEventArgs eventArgs) =>
        ScheduleDetailsClose();

    private void ScheduleDetailsClose()
    {
        _closeDetailsTimer.Stop();
        _closeDetailsTimer.Start();
    }

    private void OnCloseDetailsDelayElapsed(object? sender, EventArgs eventArgs)
    {
        _closeDetailsTimer.Stop();
        CloseDetailsIfPointerLeft();
    }

    private void CloseDetailsIfPointerLeft()
    {
        if (!RootBorder.IsMouseOver && !DetailsPanel.IsMouseOver)
        {
            CloseDetails();
        }
    }

    private void CloseDetails()
    {
        _closeDetailsTimer.Stop();
        DetailsPopup.IsOpen = false;
    }

    private static bool CanOpenDetails(OverlayPlacement placement) =>
        placement.Mode == OverlayMode.ExternalRail;

    private static bool CanKeepDetailsOpen(OverlayPlacement placement) =>
        CanOpenDetails(placement) && placement.Opacity >= 0.99;
}
