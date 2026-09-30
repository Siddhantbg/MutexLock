using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace FolderLock.App;

public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly MainWindow _window;

    public TrayIcon(MainWindow window)
    {
        _window = window;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open FolderLock", null, (_, _) => ShowWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit());

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "FolderLock",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();
    }

    public void ShowMinimizedNotice()
    {
        ShowBalloon("FolderLock", "FolderLock is still running in the tray. Double-click the icon to reopen it.");
    }

    public void ShowBalloon(string title, string message)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(3000);
    }

    public void UpdateStatus(int total, int locked)
    {
        var text = $"FolderLock: {total} total, {locked} locked";
        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    private void ShowWindow()
    {
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

    private void Exit()
    {
        _window.ExitRequested = true;
        _notifyIcon.Visible = false;
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
