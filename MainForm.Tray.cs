namespace Nook;

internal sealed partial class MainForm
{
    private void InitializeTrayIcon()
    {
        var appIcon = AppIconGenerator.GetAppIcon();
        Icon = appIcon;
        _notifyIcon.Icon = appIcon;

        _notifyIcon.Text = "Nook — GPU monitor";
        _notifyIcon.Visible = true;
        _notifyIcon.DoubleClick += (_, _) => RestoreFromTray();

        _trayOverlayItem = new ToolStripMenuItem("Overlay Visible", null, (_, _) => ToggleOverlayVisibility()) { Checked = _settings.OverlayVisible };
        _trayLockItem = new ToolStripMenuItem("Lock Overlay", null, (_, _) => ToggleOverlayLock()) { Checked = _settings.OverlayLocked };

        _trayMenu.Items.Add("Show / Hide Main Window", null, (_, _) => ToggleWindowVisibility());
        _trayMenu.Items.Add("-");
        _trayMenu.Items.Add(_trayOverlayItem);
        _trayMenu.Items.Add(_trayLockItem);
        _trayMenu.Items.Add("-");
        _trayMenu.Items.Add("Exit", null, (_, _) =>
        {
            _exitRequested = true;
            Close();
        });

        _notifyIcon.ContextMenuStrip = _trayMenu;
    }

    private void ToggleWindowVisibility()
    {
        if (Visible && WindowState != FormWindowState.Minimized)
        {
            Hide();
        }
        else
        {
            RestoreFromTray();
        }
    }

    /// <summary>
    /// Called on a thread pool thread when Nook is launched again. Running it twice would only start
    /// and exit, so the existing window is brought forward instead, wherever it was hidden.
    /// </summary>
    internal void ShowForSecondLaunch()
    {
        try
        {
            BeginInvoke(RestoreFromTray);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // The window has no handle yet or is closing; there is nothing to bring forward.
        }
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        ShowInTaskbar = true;
        BringToFront();
        Activate();
    }

    private void OnResize(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized && _settings.MinimizeToTray)
        {
            ShowInTaskbar = false;
            Hide();
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_settings.CloseToTray && !_exitRequested && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            ShowInTaskbar = false;
            Hide();

            if (!_hasShownTrayBalloon)
            {
                _hasShownTrayBalloon = true;
                _notifyIcon.ShowBalloonTip(2000, "Nook", "Application is running in the system tray.", ToolTipIcon.Info);
            }
        }
        else
        {
            _saveTimer.Stop();
            SaveSettings();
        }
    }
}
