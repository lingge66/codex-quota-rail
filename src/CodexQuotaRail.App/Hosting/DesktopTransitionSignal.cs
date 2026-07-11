namespace CodexQuotaRail.App.Hosting;

public sealed class DesktopTransitionSignal : IDesktopTransitionSignal
{
    private readonly ISystemEventSource _source;
    private int _disposed;

    public DesktopTransitionSignal(ISystemEventSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _source.PowerChanged += OnPowerChanged;
        _source.NetworkAvailable += OnNetworkAvailable;
        _source.TaskbarRestarted += OnTaskbarRestarted;
    }

    public event EventHandler? Paused;

    public event EventHandler? Resumed;

    public event EventHandler? NetworkAvailable;

    public event EventHandler? TaskbarRestarted;

    public bool IsPaused { get; private set; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _source.PowerChanged -= OnPowerChanged;
        _source.NetworkAvailable -= OnNetworkAvailable;
        _source.TaskbarRestarted -= OnTaskbarRestarted;
        _source.Dispose();
    }

    private void OnPowerChanged(object? sender, SystemPowerTransition transition)
    {
        if (transition == SystemPowerTransition.Suspend)
        {
            IsPaused = true;
            Paused?.Invoke(this, EventArgs.Empty);
            return;
        }

        IsPaused = false;
        Resumed?.Invoke(this, EventArgs.Empty);
    }

    private void OnNetworkAvailable(object? sender, EventArgs eventArgs) =>
        NetworkAvailable?.Invoke(this, EventArgs.Empty);

    private void OnTaskbarRestarted(object? sender, EventArgs eventArgs) =>
        TaskbarRestarted?.Invoke(this, EventArgs.Empty);
}
