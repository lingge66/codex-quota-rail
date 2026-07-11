using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace CodexQuotaRail.App.Rail;

public partial class RailWindow
{
    private const int GwlHwndParent = -8;
    private const int GwlExStyle = -20;
    private const int MaNoActivate = 3;
    private const int WmMouseActivate = 0x0021;
    private const int WmLeftButtonUp = 0x0202;
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExToolWindow = 0x00000080L;
    private nint _nativeHandle;
    private nint _ownerHandle;
    private HwndSource? _windowMessageSource;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _nativeHandle = new WindowInteropHelper(this).Handle;
        _windowMessageSource = HwndSource.FromHwnd(_nativeHandle);
        _windowMessageSource?.AddHook(OnWindowMessage);
        var extendedStyle = GetWindowLongPtr(_nativeHandle, GwlExStyle);
        var nextStyle = extendedStyle | new nint(WsExToolWindow | WsExNoActivate);
        SetWindowLongPtrChecked(_nativeHandle, GwlExStyle, nextStyle);
        ApplyOwnerWindow();
    }

    private nint OnWindowMessage(
        nint handle,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        if (message == WmMouseActivate)
        {
            handled = true;
            return MaNoActivate;
        }

        if (message == WmLeftButtonUp)
        {
            ToggleDetails();
            handled = true;
        }

        return 0;
    }

    private void RemoveWindowMessageHook()
    {
        _windowMessageSource?.RemoveHook(OnWindowMessage);
        _windowMessageSource = null;
    }

    private void SetOwnerWindow(nint ownerHandle)
    {
        if (_ownerHandle == ownerHandle)
        {
            return;
        }

        _ownerHandle = ownerHandle;
        ApplyOwnerWindow();
    }

    private void ApplyOwnerWindow()
    {
        if (_nativeHandle != 0)
        {
            SetWindowLongPtrChecked(_nativeHandle, GwlHwndParent, _ownerHandle);
        }
    }

    private static nint GetWindowLongPtr(nint handle, int index) =>
        nint.Size == 8 ? GetWindowLongPtr64(handle, index) : GetWindowLong32(handle, index);

    private static void SetWindowLongPtrChecked(nint handle, int index, nint value)
    {
        Marshal.SetLastPInvokeError(0);
        var previous = nint.Size == 8
            ? SetWindowLongPtr64(handle, index, value)
            : SetWindowLong32(handle, index, checked((int)value));
        if (previous == 0 && Marshal.GetLastPInvokeError() != 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(nint handle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(nint handle, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint handle, int index, nint value);
}
