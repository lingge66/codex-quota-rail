using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CodexQuotaRail.Windows.Overlay;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace CodexQuotaRail.App.Rail;

public partial class RailWindow : Window
{
    private readonly DispatcherTimer _hoverTimer;
    private PendingPlacement? _pendingPlacement;
    private bool _closed;
    private bool _renderQueued;
    private int _shimmerVersion;

    public RailWindow(RailViewModel? viewModel = null)
    {
        InitializeComponent();
        ViewModel = viewModel ?? new RailViewModel();
        DataContext = ViewModel;
        _shimmerVersion = ViewModel.ShimmerVersion;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _hoverTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _hoverTimer.Tick += OnHoverDelayElapsed;
    }

    public RailViewModel ViewModel { get; }

    public void QueuePlacement(OverlayPlacement placement, double dpiScale)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(
                () => QueuePlacement(placement, dpiScale),
                DispatcherPriority.Render);
            return;
        }

        if (_closed)
        {
            return;
        }

        _pendingPlacement = new PendingPlacement(
            placement,
            double.IsFinite(dpiScale) && dpiScale > 0 ? dpiScale : 1.0);
        if (_renderQueued)
        {
            return;
        }

        _renderQueued = true;
        CompositionTarget.Rendering += OnCompositionRendering;
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _hoverTimer.Stop();
        _hoverTimer.Tick -= OnHoverDelayElapsed;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        CompositionTarget.Rendering -= OnCompositionRendering;
        base.OnClosed(e);
    }

    private void OnCompositionRendering(object? sender, EventArgs eventArgs)
    {
        CompositionTarget.Rendering -= OnCompositionRendering;
        _renderQueued = false;
        var pending = _pendingPlacement;
        _pendingPlacement = null;
        if (pending is null || _closed)
        {
            return;
        }

        ApplyPlacement(pending);
    }

    private void ApplyPlacement(PendingPlacement pending)
    {
        var placement = pending.Placement;
        if (placement.Mode == OverlayMode.Hidden)
        {
            DetailsPopup.IsOpen = false;
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        var scale = pending.DpiScale;
        Left = placement.Bounds.Left / scale;
        Top = placement.Bounds.Top / scale;
        Width = Math.Max(1, placement.Bounds.Width / scale);
        Height = Math.Max(1, placement.Bounds.Height / scale);
        ViewModel.SetViewportWidth(Width);
        SetOpacity(placement.Opacity);
        if (!IsVisible)
        {
            Show();
        }
    }

    private void SetOpacity(double target)
    {
        var normalized = Math.Clamp(target, 0, 1);
        var current = Opacity;
        BeginAnimation(OpacityProperty, null);
        Opacity = normalized;
        if (ViewModel.ReduceMotion || Math.Abs(current - normalized) < 0.01)
        {
            return;
        }

        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(current, normalized, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(RailViewModel.ReduceMotion) &&
            ViewModel.ReduceMotion)
        {
            StopShimmer();
            return;
        }

        if (eventArgs.PropertyName != nameof(RailViewModel.ShimmerVersion) ||
            ViewModel.ReduceMotion ||
            _shimmerVersion == ViewModel.ShimmerVersion)
        {
            return;
        }

        _shimmerVersion = ViewModel.ShimmerVersion;
        StartShimmer();
    }

    private void StartShimmer()
    {
        if (ShimmerOverlay.RenderTransform is not TranslateTransform transform)
        {
            return;
        }

        var width = Math.Max(120, ActualWidth);
        transform.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(-width * 0.25, width, TimeSpan.FromMilliseconds(1200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
            },
            HandoffBehavior.SnapshotAndReplace);
        var opacity = new DoubleAnimationUsingKeyFrames();
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        opacity.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                0.28,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(350))));
        opacity.KeyFrames.Add(
            new EasingDoubleKeyFrame(
                0,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1200))));
        ShimmerOverlay.BeginAnimation(
            OpacityProperty,
            opacity,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void StopShimmer()
    {
        ShimmerOverlay.BeginAnimation(OpacityProperty, null);
        ShimmerOverlay.Opacity = 0;
        if (ShimmerOverlay.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = 0;
        }
    }

    private void OnRailMouseEnter(object sender, MouseEventArgs eventArgs)
    {
        _hoverTimer.Stop();
        _hoverTimer.Start();
    }

    private void OnRailMouseLeave(object sender, MouseEventArgs eventArgs)
    {
        _hoverTimer.Stop();
        _ = Dispatcher.BeginInvoke(
            CloseDetailsIfPointerLeft,
            DispatcherPriority.Background);
    }

    private void OnHoverDelayElapsed(object? sender, EventArgs eventArgs)
    {
        _hoverTimer.Stop();
        if (RootBorder.IsMouseOver && !_closed)
        {
            DetailsPopup.IsOpen = true;
        }
    }

    private void OnDetailsMouseLeave(object sender, MouseEventArgs eventArgs) =>
        CloseDetailsIfPointerLeft();

    private void CloseDetailsIfPointerLeft()
    {
        if (!RootBorder.IsMouseOver && !DetailsPopup.IsMouseOver)
        {
            DetailsPopup.IsOpen = false;
        }
    }

    private sealed record PendingPlacement(
        OverlayPlacement Placement,
        double DpiScale);
}
