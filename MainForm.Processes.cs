namespace Nook;

internal sealed partial class MainForm
{
    /// <summary>
    /// Rebuilds the process list off the UI thread; enumerating a few hundred processes
    /// takes long enough to be felt as a stutter in the overlay if it runs inline.
    /// </summary>
    private async void RefreshProcesses()
    {
        if (_isLoadingProcesses)
        {
            return;
        }

        _isLoadingProcesses = true;
        try
        {
            var processes = await Task.Run(ProcessCatalog.List);
            if (IsDisposed || Disposing)
            {
                return;
            }

            _allProcesses = processes;

            // Reconcile against the unfiltered list: a monitored process hidden by the search or the
            // VRAM filter is still running, and must not be mistaken for a restart.
            var reconciled = _processTracker.Reconcile(processes, ProcessCatalog.TryGetStartTime);
            UpdateSampleRequest();
            if (reconciled is TrackerEvent.Attached or TrackerEvent.Reattached && _processTracker.Current is { } process)
            {
                RememberProcessName(process.Name);
                ResetStatistics();
                SetStatus(reconciled == TrackerEvent.Attached
                    ? $"Monitoring {process.Name} (PID {process.Pid})."
                    : $"Reattached to restarted {process.Name} (PID {process.Pid}).");
            }

            ApplyProcessFilters();
        }
        catch (Exception ex)
        {
            Log.Error("The running process list could not be read.", ex);
            SetStatus($"The running process list could not be read: {ex.Message}");
        }
        finally
        {
            _isLoadingProcesses = false;
        }
    }

    private void ApplyProcessFilters()
    {
        if (_isRefreshingProcesses)
        {
            return;
        }

        var searchText = _searchBox.Text.Trim();
        var onlyVram = _vramFilterCheck.Checked;

        _filteredProcesses = _allProcesses.Where(p =>
        {
            if (onlyVram && !_activeVramPids.Contains(p.Pid))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(searchText) && !p.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }).ToList();

        // The monitored process stays in the picker even when the filters hide it, so the
        // selection never goes blank while Nook is still measuring it.
        var monitored = _processTracker.Current;
        var items = new List<object>(_filteredProcesses.Count + 1);
        if (monitored is not null && !_filteredProcesses.Contains(monitored))
        {
            items.Add(monitored);
        }

        items.AddRange(_filteredProcesses);

        _isRefreshingProcesses = true;
        _processSelector.BeginUpdate();
        try
        {
            _processSelector.Items.Clear();
            _processSelector.Items.AddRange(items.ToArray());
            _processSelector.SelectedItem = monitored;
        }
        finally
        {
            _processSelector.EndUpdate();
            _isRefreshingProcesses = false;
        }
    }

    private void SelectProcess()
    {
        if (_isRefreshingProcesses)
        {
            return;
        }

        if (_processSelector.SelectedItem is ProcessItem process)
        {
            AttachTo(process, true);
            SetStatus($"Monitoring {process.Name} (PID {process.Pid}).");
        }
        else
        {
            ClearProcessSelection();
        }
    }

    private void AttachTo(ProcessItem process, bool resetStatistics)
    {
        _processTracker.Attach(process, ProcessCatalog.TryGetStartTime(process.Pid));
        UpdateSampleRequest();
        RememberProcessName(process.Name);
        if (resetStatistics)
        {
            ResetStatistics();
        }
    }

    /// <summary>Saves the monitored name so it is followed again after Nook restarts.</summary>
    private void RememberProcessName(string name)
    {
        if (_settings.SelectedProcessName != name)
        {
            _settings.SelectedProcessName = name;
            RequestSave();
        }
    }

    private void ClearProcessSelection()
    {
        _processTracker.Detach(forget: true);
        UpdateSampleRequest();
        if (_settings.SelectedProcessName is not null)
        {
            _settings.SelectedProcessName = null;
            RequestSave();
        }

        ResetStatistics();
    }

    private void ResetStatistics()
    {
        _statistics.Reset();
        _currentValue.Text = "—";
        _peakValue.Text = "—";
        _averageValue.Text = "—";
    }

}
