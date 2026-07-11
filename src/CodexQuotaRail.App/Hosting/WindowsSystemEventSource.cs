using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CodexQuotaRail.App.Hosting;

public sealed class WindowsSystemEventSource : ISystemEventSource
{
    private readonly TaskbarMessageWindow _taskbarWindow = new();
    private int _disposed;

    public WindowsSystemEventSource()
    {
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        _taskbarWindow.Restarted += OnTaskbarRestarted;
    }

    public event EventHandler<SystemPowerTransition>? PowerChanged;

    public event EventHandler? NetworkAvailable;

    public event EventHandler? TaskbarRestarted;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _taskbarWindow.Restarted -= OnTaskbarRestarted;
        _taskbarWindow.Dispose();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs eventArgs)
    {
        var transition = eventArgs.Mode switch
        {
            PowerModes.Suspend => SystemPowerTransition.Suspend,
            PowerModes.Resume => SystemPowerTransition.Resume,
            _ => (SystemPowerTransition?)null,
        };
        if (transition is { } value)
        {
            PowerChanged?.Invoke(this, value);
        }
    }

    private void OnNetworkAvailabilityChanged(
        object? sender,
        NetworkAvailabilityEventArgs eventArgs)
    {
        if (eventArgs.IsAvailable)
        {
            NetworkAvailable?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnTaskbarRestarted(object? sender, EventArgs eventArgs) =>
        TaskbarRestarted?.Invoke(this, EventArgs.Empty);

    private sealed class TaskbarMessageWindow : NativeWindow, IDisposable
    {
        private readonly uint _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        private int _disposed;

        public TaskbarMessageWindow()
        {
            CreateHandle(
                new CreateParams
                {
                    Caption = "CodexQuotaRail.TaskbarMonitor",
                    Parent = new nint(-3),
                });
        }

        public event EventHandler? Restarted;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            DestroyHandle();
        }

        protected override void WndProc(ref Message message)
        {
            if ((uint)message.Msg == _taskbarCreatedMessage)
            {
                Restarted?.Invoke(this, EventArgs.Empty);
            }

            base.WndProc(ref message);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string message);
    }
}
