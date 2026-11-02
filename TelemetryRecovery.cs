namespace Nook;

/// <summary>
/// Decides when the vendor libraries must be reopened. A driver reset (TDR) or a driver update
/// invalidates NVML device handles and the ADL context for good, so without a reopen a GPU that
/// lost its readings would stay blank until Nook restarted. Only an adapter that has answered
/// before can trigger it: one that never had a sensor, such as an Intel iGPU, is not a failure.
/// </summary>
internal sealed class TelemetryRecovery
{
    private readonly int _emptySamplesBeforeReopen;
    private readonly int _minSamplesBetweenReopens;
    private readonly int _maxSamplesBetweenReopens;
    private readonly HashSet<GpuLuid> _answered = [];
    private int _emptySamples;
    private int _samplesSinceReopen;
    private int _samplesBetweenReopens;
    private bool _reopenedWithoutReading;

    /// <param name="emptySamplesBeforeReopen">Consecutive empty samples, for an adapter that answered before, that count as lost readings.</param>
    /// <param name="minSamplesBetweenReopens">Samples between the first reopens. Opening NVML can stall for seconds, so a GPU that stays silent must not be reopened every second.</param>
    /// <param name="maxSamplesBetweenReopens">The interval doubles after every reopen that brought nothing back, up to this many samples.</param>
    public TelemetryRecovery(int emptySamplesBeforeReopen, int minSamplesBetweenReopens, int maxSamplesBetweenReopens)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(emptySamplesBeforeReopen, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(minSamplesBetweenReopens, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSamplesBetweenReopens, minSamplesBetweenReopens);
        _emptySamplesBeforeReopen = emptySamplesBeforeReopen;
        _minSamplesBetweenReopens = minSamplesBetweenReopens;
        _maxSamplesBetweenReopens = maxSamplesBetweenReopens;
        _samplesBetweenReopens = minSamplesBetweenReopens;
        _samplesSinceReopen = minSamplesBetweenReopens;
    }

    public static bool HasReading(GpuTelemetrySample sample) =>
        sample.TempCelsius.HasValue || sample.CoreClockMhz.HasValue || sample.UsagePercent.HasValue;

    /// <summary>Records one sample for <paramref name="luid"/> and returns true when the libraries should be reopened now.</summary>
    public bool ShouldReopen(GpuLuid luid, GpuTelemetrySample sample)
    {
        if (_samplesSinceReopen < _maxSamplesBetweenReopens)
        {
            _samplesSinceReopen++;
        }

        if (HasReading(sample))
        {
            _answered.Add(luid);
            _emptySamples = 0;
            _samplesBetweenReopens = _minSamplesBetweenReopens;
            _reopenedWithoutReading = false;
            return false;
        }

        if (!_answered.Contains(luid))
        {
            _emptySamples = 0;
            return false;
        }

        _emptySamples++;
        if (_emptySamples < _emptySamplesBeforeReopen || _samplesSinceReopen < _samplesBetweenReopens)
        {
            return false;
        }

        _emptySamples = 0;
        _samplesSinceReopen = 0;
        if (_reopenedWithoutReading)
        {
            _samplesBetweenReopens = (int)Math.Min(2L * _samplesBetweenReopens, _maxSamplesBetweenReopens);
        }

        _reopenedWithoutReading = true;
        return true;
    }
}
