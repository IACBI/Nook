namespace Nook;

internal sealed partial class MainForm
{
    private void CreateOverlay()
    {
        _overlay = new OverlayForm { Locked = _settings.OverlayLocked };
        if (_settings.OverlayX != int.MinValue && _settings.OverlayY != int.MinValue)
        {
            _overlay.RestoreLocation(new Point(_settings.OverlayX, _settings.OverlayY));
        }
        else
        {
            _overlay.MoveToCorner(OverlayCorner.TopRight);
        }

        _overlay.LocationChanged += (_, _) => RequestSave();
        _overlayVisible.Checked = _settings.OverlayVisible;
        _overlayLocked.Checked = _settings.OverlayLocked;
        UpdateOverlay();
        if (_settings.OverlayVisible)
        {
            _overlay.Show();
        }
    }

    private void UpdateOverlay()
    {
        if (_overlay is null || !_settings.OverlayVisible)
        {
            return;
        }

        // An integrated GPU has no dedicated pool and a parked discrete GPU keeps almost
        // nothing in it, so the overlay reports whichever pool the memory is actually in.
        var useShared = _lastGpuDedicatedBytes == 0;
        _overlay.UpdateMetrics(
            _lastGpuName,
            _lastGpuUsage,
            useShared ? "SHARED" : "VRAM",
            useShared ? _lastGpuShared : _lastGpuMemory,
            _lastGpuTemp,
            _lastGpuClock,
            _processTracker.Current?.Name,
            useShared ? _lastProcessShared : _lastProcessMemory);
    }

    private void ToggleOverlayVisibility() => SetOverlayVisibility(!_overlayVisible.Checked);

    private void SetOverlayVisibility(bool visible)
    {
        if (_settings.OverlayVisible != visible)
        {
            _settings.OverlayVisible = visible;
            RequestSave();
        }

        if (_overlayVisible.Checked != visible)
        {
            _overlayVisible.Checked = visible;
        }

        if (_trayOverlayItem is not null)
        {
            _trayOverlayItem.Checked = visible;
        }

        if (_overlay is null) return;
        if (visible) _overlay.Show();
        else _overlay.Hide();
    }

    private void ToggleOverlayLock() => SetOverlayLock(!_overlayLocked.Checked);

    private void SetOverlayLock(bool locked)
    {
        if (_settings.OverlayLocked != locked)
        {
            _settings.OverlayLocked = locked;
            RequestSave();
        }

        if (_overlayLocked.Checked != locked)
        {
            _overlayLocked.Checked = locked;
        }

        if (_trayLockItem is not null)
        {
            _trayLockItem.Checked = locked;
        }

        if (_overlay is not null)
        {
            _overlay.Locked = locked;
            UpdateOverlay();
            SetStatus(locked ? "Overlay is click-through and locked." : "Overlay adjustment mode: drag it to any position.");
        }
    }

    private void ShowCornerMenu(Control anchor)
    {
        _cornerMenu.Show(anchor, new Point(0, anchor.Height));
    }

    private void AddCornerMenuItem(string text, OverlayCorner corner) =>
        _cornerMenu.Items.Add(text, null, (_, _) => _overlay?.MoveToCorner(corner));
}
