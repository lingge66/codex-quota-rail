using CodexQuotaRail.Windows.Interop;

namespace CodexQuotaRail.Windows.Windows;

public sealed class WindowNativeApi : IWindowNativeApi
{
    private readonly Dictionary<nint, NativeMethods.WinEventProc> _callbacks = [];
    private readonly object _sync = new();

    public IReadOnlyList<nint> EnumWindows()
    {
        var handles = new List<nint>();
        NativeMethods.EnumWindows(
            (handle, _) =>
            {
                handles.Add(handle);
                return true;
            },
            0);
        return handles;
    }

    public uint GetWindowProcessId(nint handle)
    {
        var threadId = NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        return threadId == 0 ? 0 : processId;
    }

    public bool IsMainWindow(nint handle)
    {
        if (NativeMethods.GetWindow(handle, NativeMethods.GwOwner) != 0)
        {
            return false;
        }

        var extendedStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle);
        return ((long)extendedStyle & NativeMethods.WsExToolWindow) == 0 &&
               TryGetWindowRect(handle, out var bounds) &&
               bounds.Width > 0 &&
               bounds.Height > 0;
    }

    public WindowProcessIdentity? GetProcessIdentity(nint handle)
    {
        var processId = GetWindowProcessId(handle);
        return processId == 0 ? null : WindowProcessIdentityReader.Read(processId);
    }

    public bool TryGetWindowRect(nint handle, out PixelRect bounds)
    {
        if (!NativeMethods.GetWindowRect(handle, out var rect))
        {
            bounds = default;
            return false;
        }

        bounds = ToPixelRect(rect);
        return true;
    }

    public bool IsWindowVisible(nint handle) => NativeMethods.IsWindowVisible(handle);

    public bool IsIconic(nint handle) => NativeMethods.IsIconic(handle);

    public bool IsZoomed(nint handle) => NativeMethods.IsZoomed(handle);

    public nint GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    public PixelRect GetMonitorWorkArea(nint handle)
    {
        var monitor = NativeMethods.MonitorFromWindow(
            handle,
            NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo
        {
            Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>(),
        };
        return monitor != 0 && NativeMethods.GetMonitorInfo(monitor, ref info)
            ? ToPixelRect(info.WorkArea)
            : default;
    }

    public uint GetDpiForWindow(nint handle) => NativeMethods.GetDpiForWindow(handle);

    public nint SetWinEventHook(
        TrackedWindowEvent windowEvent,
        Action<TrackedWindowEvent, nint> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        NativeMethods.WinEventProc nativeCallback =
            (_, nativeEvent, handle, objectId, childId, _, _) =>
            {
                if (handle == 0 ||
                    (nativeEvent >= 0x8000 &&
                     (objectId != NativeMethods.ObjIdWindow || childId != 0)))
                {
                    return;
                }

                callback((TrackedWindowEvent)nativeEvent, handle);
            };
        var hook = NativeMethods.SetWinEventHook(
            (uint)windowEvent,
            (uint)windowEvent,
            0,
            nativeCallback,
            0,
            0,
            NativeMethods.WineventOutOfContext);
        if (hook != 0)
        {
            lock (_sync)
            {
                _callbacks.Add(hook, nativeCallback);
            }
        }

        return hook;
    }

    public bool UnhookWinEvent(nint hook)
    {
        var result = NativeMethods.UnhookWinEvent(hook);
        lock (_sync)
        {
            _callbacks.Remove(hook);
        }

        return result;
    }

    private static PixelRect ToPixelRect(NativeMethods.NativeRect rect)
    {
        var width = Math.Clamp((long)rect.Right - rect.Left, 0, int.MaxValue);
        var height = Math.Clamp((long)rect.Bottom - rect.Top, 0, int.MaxValue);
        return new PixelRect(rect.Left, rect.Top, (int)width, (int)height);
    }
}
