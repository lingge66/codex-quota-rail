using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CodexQuotaRail.Core.Quotas;
using CodexQuotaRail.Windows.Overlay;

namespace CodexQuotaRail.App.Rail;

public sealed partial class RailViewModel : INotifyPropertyChanged
{
    private readonly ObservableCollection<QuotaTrackViewModel> _tracks = [];
    private readonly Dictionary<string, int?> _previousAvailable = [];
    private readonly TimeProvider _timeProvider;
    private bool _hasExhaustedTrack;
    private bool _isCompact;
    private bool _isMarqueeActive;
    private bool _reduceMotion;
    private string _statusText = "正在连接 Codex";
    private DateTimeOffset? _updatedAt;
    private double _viewportWidth = double.PositiveInfinity;
    private int _shimmerVersion;

    public RailViewModel(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        Tracks = new ReadOnlyObservableCollection<QuotaTrackViewModel>(_tracks);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ReadOnlyObservableCollection<QuotaTrackViewModel> Tracks { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public bool IsCompact
    {
        get => _isCompact;
        private set
        {
            if (SetField(ref _isCompact, value))
            {
                OnPropertyChanged(nameof(ShowLabels));
                OnPropertyChanged(nameof(ShowResetText));
                OnPropertyChanged(nameof(ShowTrackText));
            }
        }
    }

    public bool ShowLabels => !IsCompact;

    public bool ShowResetText => ShowLabels && _viewportWidth >= 520;

    public bool ShowTrackText => ShowLabels && _viewportWidth >= 360;

    public bool HasSingleTrack => Tracks.Count == 1;

    public bool HasTracks => Tracks.Count > 0;

    public int ShimmerVersion
    {
        get => _shimmerVersion;
        private set => SetField(ref _shimmerVersion, value);
    }

    public bool IsMarqueeActive
    {
        get => _isMarqueeActive;
        private set => SetField(ref _isMarqueeActive, value);
    }

    public bool ReduceMotion
    {
        get => _reduceMotion;
        set
        {
            if (SetField(ref _reduceMotion, value))
            {
                UpdateMarqueeState();
            }
        }
    }

    public DateTimeOffset? UpdatedAt
    {
        get => _updatedAt;
        private set => SetField(ref _updatedAt, value);
    }

    public void Apply(QuotaDisplayState state, OverlayMode mode)
    {
        ArgumentNullException.ThrowIfNull(state);
        IsCompact = mode == OverlayMode.CompactTitleBar;
        UpdatedAt = state.UpdatedAt;
        _tracks.Clear();
        var nextAvailable = new Dictionary<string, int?>(StringComparer.Ordinal);
        var crossedThreshold = false;
        foreach (var window in state.Windows.Take(2))
        {
            var track = CreateTrack(window);
            _tracks.Add(track);
            nextAvailable[track.Label] = track.AvailablePercent;
            crossedThreshold |= CrossedWarningThreshold(track);
        }

        _previousAvailable.Clear();
        foreach (var item in nextAvailable)
        {
            _previousAvailable.Add(item.Key, item.Value);
        }

        _hasExhaustedTrack = _tracks.Any(
            static track => track.State == QuotaWindowState.Exhausted);
        StatusText = CreateStatusText(state);
        OnPropertyChanged(nameof(HasSingleTrack));
        OnPropertyChanged(nameof(HasTracks));
        if (crossedThreshold && !ReduceMotion)
        {
            ShimmerVersion++;
        }

        UpdateMarqueeState();
    }

    public void SetViewportWidth(double width)
    {
        var normalized = double.IsFinite(width) ? Math.Max(0, width) : 0;
        if (Math.Abs(_viewportWidth - normalized) < 0.5)
        {
            return;
        }

        _viewportWidth = normalized;
        OnPropertyChanged(nameof(ShowResetText));
        OnPropertyChanged(nameof(ShowTrackText));
    }

    private void UpdateMarqueeState() =>
        IsMarqueeActive = _hasExhaustedTrack && !ReduceMotion;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record QuotaTrackViewModel(
    string Label,
    int? AvailablePercent,
    double WidthFraction,
    RgbColor Color,
    string ResetText,
    QuotaWindowState State,
    bool IsUnlimited,
    string ValueText);
