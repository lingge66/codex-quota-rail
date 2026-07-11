using System.Drawing;
using System.Windows.Forms;

namespace CodexQuotaRail.App.Tray;

public sealed class WindowsFormsTrayIconHost : ITrayIconHost
{
    private readonly NotifyIcon _notifyIcon;
    private ContextMenuStrip? _menu;
    private int _disposed;

    public WindowsFormsTrayIconHost()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Codex 可用额度",
            Visible = true,
        };
    }

    public event EventHandler<string>? CommandInvoked;

    public void SetMenu(IReadOnlyList<TrayMenuItemModel> menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var nextMenu = new ContextMenuStrip();
        foreach (var item in menu)
        {
            nextMenu.Items.Add(CreateItem(item));
        }

        var previousMenu = _menu;
        _menu = nextMenu;
        _notifyIcon.ContextMenuStrip = nextMenu;
        var status = menu.FirstOrDefault(item => item.Id == "status")?.Text;
        if (!string.IsNullOrWhiteSpace(status))
        {
            _notifyIcon.Text = status.Length > 63 ? status[..63] : status;
        }

        previousMenu?.Dispose();
    }

    public void RecreateIcon()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _notifyIcon.Visible = false;
        _notifyIcon.Visible = true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip = null;
        _menu?.Dispose();
        _notifyIcon.Dispose();
    }

    private ToolStripItem CreateItem(TrayMenuItemModel model)
    {
        if (model.IsSeparator)
        {
            return new ToolStripSeparator();
        }

        var item = new ToolStripMenuItem(model.Text)
        {
            Enabled = model.IsEnabled,
            Checked = model.IsChecked,
            CheckOnClick = false,
            Tag = model.Id,
        };
        foreach (var child in model.Children)
        {
            item.DropDownItems.Add(CreateItem(child));
        }

        if (model.Children.Count == 0 && model.IsEnabled)
        {
            item.Click += OnMenuItemClick;
        }

        return item;
    }

    private void OnMenuItemClick(object? sender, EventArgs eventArgs)
    {
        if (sender is ToolStripMenuItem { Tag: string commandId })
        {
            CommandInvoked?.Invoke(this, commandId);
        }
    }
}
