namespace CodexQuotaRail.Windows.Windows;

public enum TrackedWindowEvent : uint
{
    Foreground = 0x0003,
    MoveSizeEnd = 0x000B,
    MinimizeStart = 0x0016,
    MinimizeEnd = 0x0017,
    Show = 0x8002,
    Hide = 0x8003,
    LocationChange = 0x800B,
}

public sealed record WindowProcessIdentity(
    string ExecutablePath,
    string? PackageFullName,
    string? SignerSubject);

public interface IWindowNativeApi
{
    IReadOnlyList<nint> EnumWindows();

    uint GetWindowProcessId(nint handle);

    bool IsMainWindow(nint handle);

    WindowProcessIdentity? GetProcessIdentity(nint handle);

    bool TryGetWindowRect(nint handle, out PixelRect bounds);

    bool IsWindowVisible(nint handle);

    bool IsIconic(nint handle);

    bool IsZoomed(nint handle);

    nint GetForegroundWindow();

    PixelRect GetMonitorWorkArea(nint handle);

    uint GetDpiForWindow(nint handle);

    nint SetWinEventHook(
        TrackedWindowEvent windowEvent,
        Action<TrackedWindowEvent, nint> callback);

    bool UnhookWinEvent(nint hook);
}

public interface IWindowUpdateScheduler
{
    void Schedule(Action callback);
}

public sealed class SynchronizationContextWindowUpdateScheduler
    : IWindowUpdateScheduler
{
    private readonly SynchronizationContext _context;

    public SynchronizationContextWindowUpdateScheduler(
        SynchronizationContext? context = null)
    {
        _context = context ?? SynchronizationContext.Current ?? new SynchronizationContext();
    }

    public void Schedule(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _context.Post(static state => ((Action)state!).Invoke(), callback);
    }
}
