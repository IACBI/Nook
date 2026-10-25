namespace Nook;

internal sealed partial class MainForm
{
    /// <summary>
    /// Enumerates DXGI adapters off the UI thread: it is a driver call like any other, and a hang here
    /// would otherwise freeze the window before the first sample.
    /// </summary>
    private async void LoadAdapterCatalog()
    {
        try
        {
            var adapters = await Task.Run(GpuAdapterCatalog.GetAdapters);
            if (IsDisposed)
            {
                return;
            }

            foreach (var adapter in adapters)
            {
                _adapterLookup[adapter.Luid] = adapter;
            }
        }
        catch (Exception ex)
        {
            Log.Error("DXGI adapter enumeration failed.", ex);
        }

        _adapterCatalogLoaded = true;
        Log.Info(_adapterLookup.Count == 0
            ? "DXGI listed no adapters; names come from the GPU counters."
            : $"Adapters: {string.Join("; ", _adapterLookup.Values.Select(adapter => $"{adapter.Name} ({adapter.Luid})"))}");
        RefreshGpuList(null);
        UpdateSampleRequest();
    }

    /// <summary>Runs on the sampler thread; everything past the hand-off runs on the UI thread.</summary>
    private void PostSnapshot(GpuSnapshot snapshot)
    {
        try
        {
            BeginInvoke(() => ApplySnapshot(snapshot));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // The window is closing, or its handle is being recreated; the next sample follows within a second.
        }
    }

    /// <summary>Tells the sampler which GPU and process the UI shows now. Cheap: an unchanged request is not republished.</summary>
    private void UpdateSampleRequest()
    {
        if (_sampler is null)
        {
            return;
        }

        var gpu = _gpuSelector.SelectedItem as GpuAdapterItem;
        var process = _processTracker.Current;
        var request = new SampleRequest(gpu?.Luid, gpu?.Name, process?.Pid, process?.Name);
        if (request != _sampler.Request)
        {
            _sampler.Request = request;
        }
    }

    private void CheckSamplerHealth()
    {
        if (_sampler is null)
        {
            return;
        }

        var busy = _sampler.BusyFor;
        if (busy < SamplerStallThreshold)
        {
            return;
        }

        if (!_samplerStalled)
        {
            _samplerStalled = true;
            Log.Warn($"The GPU driver has not answered for {busy.TotalSeconds:0} s; readings are paused.");
            ClearCurrentValues();
            UpdateOverlay();
        }

        SetStatus($"The GPU driver is not responding ({busy.TotalSeconds:0} s). Readings resume when it answers.");
    }

    private void ApplySnapshot(GpuSnapshot snapshot)
    {
        if (IsDisposed)
        {
            return;
        }

        if (_samplerStalled)
        {
            _samplerStalled = false;
            Log.Info("The GPU driver is answering again.");
            // Otherwise the stall message stays up until something else happens to replace it.
            SetStatus(_hotkeyWarning ?? "Ready. Values update once per second.");
        }

        try
        {
            // The snapshot answers for the PID it was asked about; right after a new pick it may still describe the old one.
            var monitored = _processTracker.Current;
            if (monitored is not null && snapshot.Request.Pid == monitored.Pid && snapshot.Process.Checked &&
                _processTracker.CheckAlive(_ => snapshot.Process.Live) == TrackerEvent.Exited)
            {
                ResetStatistics();
                SetStatus($"{monitored.Name} exited. Nook will follow it when it starts again.");
            }

            if (!snapshot.IsAvailable)
            {
                var error = snapshot.Error ?? "Windows GPU counters are unavailable.";
                LogReadError(error, snapshot.ErrorException);
                SetStatus(error);
                ClearCurrentValues();
                UpdateOverlay();
                return;
            }

            if (_lastLoggedReadError is not null)
            {
                Log.Info("Windows GPU counters recovered.");
                _lastLoggedReadError = null;
            }

            var processSamples = snapshot.ProcessDedicatedBytes;
            var processShared = snapshot.ProcessSharedBytes;
            _activeVramPids.Clear();
            _activeVramPids.UnionWith(processSamples.Keys);
            _activeVramPids.UnionWith(processShared.Keys);
            RefreshGpuList(snapshot);
            SampleSelectedProcess(processSamples, processShared);
            SampleSelectedGpu(snapshot);
            if (_processTracker.Current is { } tracked && processSamples.ContainsKey(tracked.Pid))
            {
                SetStatus($"Monitoring {tracked.Name} (PID {tracked.Pid}).");
            }
            UpdateOverlay();
        }
        catch (Exception ex)
        {
            LogReadError($"A GPU sample could not be shown: {ex.Message}", ex);
            ClearCurrentValues();
            SetStatus($"A GPU sample could not be shown: {ex.Message}");
            UpdateOverlay();
        }
        finally
        {
            UpdateSampleRequest();
        }
    }

    /// <summary>Logs a sampling failure once per distinct message; the sampler would otherwise repeat it every second.</summary>
    private void LogReadError(string error, Exception? exception)
    {
        if (error == _lastLoggedReadError)
        {
            return;
        }

        _lastLoggedReadError = error;
        if (exception is null)
        {
            Log.Warn(error);
        }
        else
        {
            Log.Error(error, exception);
        }
    }

    private void SampleSelectedProcess(IReadOnlyDictionary<int, long> dedicated, IReadOnlyDictionary<int, long> shared)
    {
        _lastProcessMemory = null;
        _lastProcessShared = null;
        if (_processTracker.Current is not { } monitored)
        {
            return;
        }

        var hasDedicated = dedicated.TryGetValue(monitored.Pid, out var dedicatedBytes);
        var hasShared = shared.TryGetValue(monitored.Pid, out var sharedBytes);
        if (!hasDedicated && !hasShared)
        {
            _currentValue.Text = "—";
            _processSharedValue.Text = "—";
            SetStatus("Windows has no GPU memory sample for this process.");
            return;
        }

        _statistics.Add(dedicatedBytes);
        _currentValue.Text = DisplayFormat.Bytes(dedicatedBytes);
        _peakValue.Text = DisplayFormat.Bytes(_statistics.PeakBytes);
        _averageValue.Text = DisplayFormat.Bytes(_statistics.AverageBytes);
        _processSharedValue.Text = DisplayFormat.Bytes(sharedBytes);
        _lastProcessMemory = _currentValue.Text;
        _lastProcessShared = _processSharedValue.Text;
    }

    private void SampleSelectedGpu(GpuSnapshot snapshot)
    {
        var dedicated = snapshot.AdapterDedicatedBytes;
        var shared = snapshot.AdapterSharedBytes;
        var usage = snapshot.AdapterUsagePercent;
        _lastGpuName = "GPU unavailable";
        _lastGpuUsage = "—";
        _lastGpuMemory = "—";
        _lastGpuShared = "—";
        _lastGpuTemp = "—";
        _lastGpuClock = "—";
        _lastGpuDedicatedBytes = 0;

        if (_gpuSelector.SelectedItem is not GpuAdapterItem gpu)
        {
            _gpuUsageValue.Text = "—";
            _gpuMemoryValue.Text = "—";
            _gpuSharedValue.Text = "—";
            _gpuTempValue.Text = "—";
            _gpuClockValue.Text = "—";
            return;
        }

        _lastGpuName = gpu.Name;
        // Driver readings belong to the GPU the sample was taken for; right after a switch that is still the old one.
        var telemetryIsCurrent = snapshot.Request.Gpu == gpu.Luid;
        var telemetry = telemetryIsCurrent ? snapshot.Telemetry : default;
        var missingSensorText = telemetryIsCurrent ? NoDriverSensorText : "—";

        if (usage.TryGetValue(gpu.Luid, out var percent))
        {
            _lastGpuUsage = $"{percent:0.0}%";
            _gpuUsageValue.Text = _lastGpuUsage;
        }
        else if (telemetry.UsagePercent.HasValue)
        {
            _lastGpuUsage = $"{telemetry.UsagePercent.Value:0.0}%";
            _gpuUsageValue.Text = _lastGpuUsage;
        }
        else
        {
            _gpuUsageValue.Text = "Unavailable";
        }

        if (dedicated.TryGetValue(gpu.Luid, out var dedicatedBytes))
        {
            _lastGpuDedicatedBytes = dedicatedBytes;
            _lastGpuMemory = DisplayFormat.Bytes(dedicatedBytes);
            _gpuMemoryValue.Text = _lastGpuMemory;
        }
        else
        {
            _gpuMemoryValue.Text = "Unavailable";
        }

        if (shared.TryGetValue(gpu.Luid, out var sharedBytes))
        {
            _lastGpuShared = DisplayFormat.Bytes(sharedBytes);
            _gpuSharedValue.Text = _lastGpuShared;
        }
        else
        {
            _gpuSharedValue.Text = "Unavailable";
        }

        if (telemetry.TempCelsius.HasValue)
        {
            _lastGpuTemp = $"{telemetry.TempCelsius.Value:0}°C";
            _gpuTempValue.Text = _lastGpuTemp;
        }
        else
        {
            _gpuTempValue.Text = missingSensorText;
        }

        if (telemetry.CoreClockMhz.HasValue)
        {
            _lastGpuClock = $"{telemetry.CoreClockMhz.Value} MHz";
            _gpuClockValue.Text = _lastGpuClock;
        }
        else
        {
            _gpuClockValue.Text = missingSensorText;
        }
    }

    private void RefreshGpuList(GpuSnapshot? sample)
    {
        if (_isRefreshingGpus)
        {
            return;
        }

        var selected = (_gpuSelector.SelectedItem as GpuAdapterItem)?.Luid ?? _settings.SelectedGpu;
        var changed = _gpuSelector.Items.Count == 0;

        // Only inject raw PDH counter LUIDs once DXGI has answered and listed NO adapters.
        // The names were resolved on the sampler thread; D3DKMT is a driver call too.
        if (_adapterCatalogLoaded && _adapterLookup.Count == 0 && sample is not null)
        {
            foreach (var (luid, name) in sample.AdapterNames)
            {
                changed |= _adapterLookup.TryAdd(luid, new GpuAdapterItem(luid, name));
            }
        }

        if (!changed && _gpuSelector.Items.Count > 0)
        {
            return;
        }

        var adapters = _adapterLookup.Values.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
        _isRefreshingGpus = true;
        _gpuSelector.BeginUpdate();
        try
        {
            _gpuSelector.Items.Clear();
            _gpuSelector.Items.AddRange(adapters.Cast<object>().ToArray());
            var match = GpuAdapterCatalog.FindAdapter(adapters, selected, _settings.SelectedGpuName);
            _gpuSelector.SelectedItem = match;
            if (match is not null && (match.Luid != _settings.SelectedGpu || match.Name != _settings.SelectedGpuName))
            {
                _settings.SetSelectedGpu(match.Luid, match.Name);
                RequestSave();
            }
        }
        finally
        {
            _gpuSelector.EndUpdate();
            _isRefreshingGpus = false;
        }
    }

    private void SelectGpu()
    {
        if (_isRefreshingGpus)
        {
            return;
        }

        var gpu = _gpuSelector.SelectedItem as GpuAdapterItem;
        _settings.SetSelectedGpu(gpu?.Luid, gpu?.Name);
        UpdateSampleRequest();
        RequestSave();
        _gpuUsageValue.Text = "—";
        _gpuMemoryValue.Text = "—";
    }

    private void ClearCurrentValues()
    {
        _currentValue.Text = "—";
        _processSharedValue.Text = "—";
        _gpuUsageValue.Text = "—";
        _gpuMemoryValue.Text = "—";
        _gpuSharedValue.Text = "—";
        _gpuTempValue.Text = "—";
        _gpuClockValue.Text = "—";
        _lastGpuUsage = "—";
        _lastGpuMemory = "—";
        _lastGpuShared = "—";
        _lastGpuTemp = "—";
        _lastGpuClock = "—";
        _lastGpuDedicatedBytes = 0;
        _lastProcessMemory = null;
        _lastProcessShared = null;
    }
}
