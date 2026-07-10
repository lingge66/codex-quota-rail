using CodexQuotaRail.Windows.Windows;

namespace CodexQuotaRail.Windows.Tests;

internal sealed class FakeWindowNativeApi : IWindowNativeApi
{
    private readonly Dictionary<nint, HookRegistration> _hooks = [];
    private readonly List<nint> _order = [];
    private readonly Dictionary<nint, FakeWindow> _windows = [];
    private nint _nextHook = 100;

    public nint ForegroundWindow { get; set; }

    public Action? HookInstalled { get; set; }

    public int SnapshotReadCount { get; private set; }

    public IReadOnlyCollection<TrackedWindowEvent> HookedEvents =>
        _hooks.Values.Select(static registration => registration.Event).ToArray();

    public IReadOnlyCollection<nint> UnhookedHandles { get; private set; } = [];

    public void Add(FakeWindow window)
    {
        _windows.Add(window.Handle, window);
        _order.Add(window.Handle);
    }

    public FakeWindow Window(nint handle) => _windows[handle];

    public void Raise(TrackedWindowEvent windowEvent, nint handle)
    {
        foreach (var registration in _hooks.Values
                     .Where(registration => registration.Event == windowEvent)
                     .ToArray())
        {
            registration.Callback(windowEvent, handle);
        }
    }

    public IReadOnlyList<nint> EnumWindows() => [.. _order];

    public uint GetWindowProcessId(nint handle) => (uint)handle;

    public bool IsMainWindow(nint handle) => _windows[handle].IsMainWindow;

    public WindowProcessIdentity? GetProcessIdentity(nint handle)
    {
        var window = _windows[handle];
        if (window.ThrowsOnIdentity)
        {
            throw new InvalidOperationException("identity unavailable");
        }

        return window.Identity;
    }

    public bool TryGetWindowRect(nint handle, out PixelRect bounds)
    {
        SnapshotReadCount++;
        bounds = _windows[handle].Bounds;
        return _windows[handle].HasBounds;
    }

    public bool IsWindowVisible(nint handle) => _windows[handle].IsVisible;

    public bool IsIconic(nint handle) => _windows[handle].IsMinimized;

    public bool IsZoomed(nint handle) => _windows[handle].IsMaximized;

    public nint GetForegroundWindow() => ForegroundWindow;

    public PixelRect GetMonitorWorkArea(nint handle) => _windows[handle].WorkArea;

    public uint GetDpiForWindow(nint handle) => _windows[handle].Dpi;

    public nint SetWinEventHook(
        TrackedWindowEvent windowEvent,
        Action<TrackedWindowEvent, nint> callback)
    {
        var hook = _nextHook++;
        _hooks.Add(hook, new HookRegistration(windowEvent, callback));
        HookInstalled?.Invoke();
        return hook;
    }

    public bool UnhookWinEvent(nint hook)
    {
        if (!_hooks.Remove(hook))
        {
            return false;
        }

        UnhookedHandles = [.. UnhookedHandles, hook];
        return true;
    }

    private sealed record HookRegistration(
        TrackedWindowEvent Event,
        Action<TrackedWindowEvent, nint> Callback);
}

internal sealed class FakeWindow(nint handle, WindowProcessIdentity? identity)
{
    public nint Handle { get; } = handle;

    public WindowProcessIdentity? Identity { get; } = identity;

    public PixelRect Bounds { get; set; } = new(100, 100, 800, 600);

    public PixelRect WorkArea { get; set; } = new(0, 0, 1920, 1040);

    public uint Dpi { get; set; } = 96;

    public bool HasBounds { get; set; } = true;

    public bool IsMainWindow { get; set; } = true;

    public bool IsVisible { get; set; } = true;

    public bool IsMinimized { get; set; }

    public bool IsMaximized { get; set; }

    public bool ThrowsOnIdentity { get; set; }
}

internal sealed class ManualWindowUpdateScheduler : IWindowUpdateScheduler
{
    private readonly Queue<Action> _callbacks = new();

    public int PendingCount => _callbacks.Count;

    public void Schedule(Action callback) => _callbacks.Enqueue(callback);

    public void RunFrame()
    {
        var count = _callbacks.Count;
        for (var index = 0; index < count; index++)
        {
            _callbacks.Dequeue().Invoke();
        }
    }
}
