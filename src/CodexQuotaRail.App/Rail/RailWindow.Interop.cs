using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace CodexQuotaRail.App.Rail;

public partial class RailWindow
{
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000L;
    private const long WsExToolWindow = 0x00000080L;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var extendedStyle = GetWindowLongPtr(handle, GwlExStyle);
        var nextStyle = extendedStyle | new nint(WsExToolWindow | WsExNoActivate);
        SetWindowLongPtrChecked(handle, GwlExStyle, nextStyle);
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
