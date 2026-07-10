using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CodexQuotaRail.Windows.Interop;

internal static class NativeMethods
{
    internal const int AppModelErrorNoPackage = 15700;
    internal const int ErrorInsufficientBuffer = 122;
    internal const int GwlExStyle = -20;
    internal const uint GwOwner = 4;
    internal const int ObjIdWindow = 0;
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const uint MonitorDefaultToNearest = 2;
    internal const uint WineventOutOfContext = 0;
    internal const long WsExToolWindow = 0x00000080L;

    internal delegate bool EnumWindowsProc(nint handle, nint parameter);

    internal delegate void WinEventProc(
        nint hook,
        uint windowEvent,
        nint handle,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint handle, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint handle, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsZoomed(nint handle);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern nint GetWindow(nint handle, uint command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(nint handle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLong64(nint handle, int index);

    internal static nint GetWindowLongPtr(nint handle, int index) =>
        nint.Size == 8 ? GetWindowLong64(handle, index) : GetWindowLong32(handle, index);

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromWindow(nint handle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint handle);

    [DllImport("user32.dll")]
    internal static extern nint SetWinEventHook(
        uint eventMinimum,
        uint eventMaximum,
        nint module,
        WinEventProc callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(nint hook);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageName(
        SafeProcessHandle process,
        uint flags,
        char[] executableName,
        ref uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetPackageFullName(
        SafeProcessHandle process,
        ref uint packageFullNameLength,
        char[]? packageFullName);

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    internal static extern int WinVerifyTrust(
        nint window,
        [In] ref Guid actionId,
        [In] ref WinTrustData trustData);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfo
    {
        internal uint Size;
        internal NativeRect Monitor;
        internal NativeRect WorkArea;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WinTrustFileInfo
    {
        internal uint Size;
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string FilePath;
        internal nint File;
        internal nint KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WinTrustData
    {
        internal uint Size;
        internal nint PolicyCallbackData;
        internal nint SipClientData;
        internal uint UiChoice;
        internal uint RevocationChecks;
        internal uint UnionChoice;
        internal nint FileInfo;
        internal uint StateAction;
        internal nint StateData;
        internal nint UrlReference;
        internal uint ProviderFlags;
        internal uint UiContext;
        internal nint SignatureSettings;
    }
}
