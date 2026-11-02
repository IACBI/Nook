namespace Nook;

/// <summary>
/// The production sample source: PDH counters, NVML/ADL telemetry and the monitored process's
/// identity. It is created on the sampler thread and touched by no other, which is what lets
/// the native readers stay free of locks.
/// </summary>
internal sealed class NativeSampleSource : ISampleSource
{
    // Counters can be missing early in a logon started by the Run key, so keep trying.
    private const int ReaderRetryIntervalSamples = 10;
    private const int TelemetryEmptySamplesBeforeReopen = 5;
    private const int TelemetryMinSamplesBetweenReopens = 60;
    private const int TelemetryMaxSamplesBetweenReopens = 30 * 60;

    private readonly TelemetryRecovery _telemetryRecovery = new(
        TelemetryEmptySamplesBeforeReopen, TelemetryMinSamplesBetweenReopens, TelemetryMaxSamplesBetweenReopens);
    private GpuTelemetryEngine _telemetry = new();
    private bool _telemetryLost;
    private readonly Dictionary<GpuLuid, string?> _nameLookups = [];
    private readonly Dictionary<GpuLuid, string> _adapterNames = [];
    private PdhGpuMemoryReader? _reader;
    private string _readerError = "Windows GPU counters are not open yet.";
    private int _samplesWithoutReader;
    private bool _readerFailureLogged;

    public NativeSampleSource()
    {
        TryOpenReader();
    }

    public GpuSnapshot Sample(SampleRequest request)
    {
        var process = request.Pid is { } pid ? ProcessLookup.Of(ProcessCatalog.LookUp(pid, request.ProcessName ?? string.Empty)) : default;
        if (_reader is null && (++_samplesWithoutReader % ReaderRetryIntervalSamples != 0 || !TryOpenReader()))
        {
            return GpuSnapshot.Unavailable(request, _readerError, null, process);
        }

        var read = _reader!.Read();
        if (!read.IsAvailable)
        {
            return GpuSnapshot.Unavailable(request, read.Error ?? "Windows GPU counters are unavailable.", null, process);
        }

        ResolveAdapterNames(read.AdapterDedicatedBytes!.Keys);
        var telemetry = request.Gpu is { } luid ? ReadTelemetry(luid, request.GpuName) : default;
        return GpuSnapshot.FromRead(request, read, _adapterNames, telemetry, process);
    }

    private GpuTelemetrySample ReadTelemetry(GpuLuid luid, string? adapterName)
    {
        var sample = _telemetry.GetTelemetry(luid, adapterName);
        if (_telemetryRecovery.ShouldReopen(luid, sample))
        {
            if (!_telemetryLost)
            {
                _telemetryLost = true;
                Log.Warn("Driver temperature and clock readings stopped; reopening the vendor libraries, then retrying at growing intervals of up to 30 minutes.");
            }

            _telemetry.Dispose();
            _telemetry = new GpuTelemetryEngine();
            sample = _telemetry.GetTelemetry(luid, adapterName);
        }

        if (_telemetryLost && TelemetryRecovery.HasReading(sample))
        {
            _telemetryLost = false;
            Log.Info("Driver temperature and clock readings are back.");
        }

        return sample;
    }

    public void Dispose()
    {
        _reader?.Dispose();
        _telemetry.Dispose();
    }

    private bool TryOpenReader()
    {
        try
        {
            _reader = new PdhGpuMemoryReader();
            if (_readerFailureLogged)
            {
                Log.Info("Windows GPU counters became available.");
            }

            return true;
        }
        catch (Exception ex)
        {
            _readerError = $"Windows GPU counters unavailable: {ex.Message}";
            if (!_readerFailureLogged)
            {
                _readerFailureLogged = true;
                Log.Error("Windows GPU counters could not be opened; retrying every 10 seconds.", ex);
            }

            return false;
        }
    }

    /// <summary>
    /// Names adapters that only PDH knows about, for when DXGI lists nothing. Each LUID is asked
    /// once, because the D3DKMT lookup would otherwise repeat every second for an unnamed adapter.
    /// </summary>
    private void ResolveAdapterNames(IEnumerable<GpuLuid> luids)
    {
        foreach (var luid in luids)
        {
            if (_nameLookups.ContainsKey(luid))
            {
                continue;
            }

            var name = GpuAdapterCatalog.GetAdapterName(luid);
            var known = name.StartsWith("LUID 0x", StringComparison.OrdinalIgnoreCase) ? null : name;
            _nameLookups[luid] = known;
            if (known is not null)
            {
                _adapterNames[luid] = known;
            }
        }
    }
}
