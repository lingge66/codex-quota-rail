using System.ComponentModel;
using System.Security;
using System.Security.Cryptography;

namespace CodexQuotaRail.Windows.Windows;

public sealed partial class CodexWindowTracker
{
    private void RefreshSnapshot()
    {
        var foreground = _nativeApi.GetForegroundWindow();
        var candidates = new List<TrackedWindowSnapshot>();
        foreach (var handle in _nativeApi.EnumWindows())
        {
            TrackedWindowSnapshot? snapshot;
            try
            {
                snapshot = TryCreateSnapshot(handle, foreground);
            }
            catch (Exception error) when (IsInaccessibleWindow(error))
            {
                continue;
            }

            if (snapshot is not null && snapshot.IsVisible)
            {
                candidates.Add(snapshot);
            }
        }

        TrackedWindowSnapshot? next;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            if (foreground != 0 && candidates.Any(candidate => candidate.Handle == foreground))
            {
                _activationOrder[foreground] = ++_activationSequence;
            }

            next = candidates
                .OrderByDescending(candidate => ActivationOrder(candidate.Handle))
                .ThenByDescending(candidate => candidate.IsForeground)
                .FirstOrDefault();
            if (Equals(_currentSnapshot, next))
            {
                return;
            }

            _currentSnapshot = next;
        }

        RaiseSnapshotChanged(next);
    }

    private TrackedWindowSnapshot? TryCreateSnapshot(nint handle, nint foreground)
    {
        if (!_nativeApi.IsMainWindow(handle) ||
            !CodexWindowIdentityMatcher.IsMatch(_nativeApi.GetProcessIdentity(handle)) ||
            !_nativeApi.TryGetWindowRect(handle, out var bounds))
        {
            return null;
        }

        var dpi = _nativeApi.GetDpiForWindow(handle);
        return new TrackedWindowSnapshot(
            handle,
            bounds,
            _nativeApi.GetMonitorWorkArea(handle),
            dpi == 0 ? 1.0 : dpi / 96.0,
            _nativeApi.IsWindowVisible(handle),
            _nativeApi.IsIconic(handle),
            _nativeApi.IsZoomed(handle),
            handle == foreground);
    }

    private long ActivationOrder(nint handle) =>
        _activationOrder.TryGetValue(handle, out var order) ? order : 0;

    private static bool IsInaccessibleWindow(Exception error) =>
        error is ArgumentException or IOException or InvalidOperationException or
            CryptographicException or SecurityException or UnauthorizedAccessException or
            Win32Exception;

    private void RaiseSnapshotChanged(TrackedWindowSnapshot? snapshot)
    {
        var handlers = SnapshotChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (var subscriber in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<TrackedWindowSnapshot?>)subscriber)(this, snapshot);
            }
            catch
            {
            }
        }
    }
}
