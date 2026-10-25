namespace Nook;

/// <summary>Peak and running average of the monitored process's dedicated memory since it was attached.</summary>
internal sealed class MemoryStatistics
{
    private long _sampleCount;
    private double _totalBytes;

    public long PeakBytes { get; private set; }

    public bool HasSamples => _sampleCount > 0;

    /// <summary>The mean of every sample so far; zero before the first.</summary>
    public double AverageBytes => _sampleCount == 0 ? 0 : _totalBytes / _sampleCount;

    public void Add(long bytes)
    {
        _sampleCount++;
        _totalBytes += bytes;
        PeakBytes = Math.Max(PeakBytes, bytes);
    }

    public void Reset()
    {
        _sampleCount = 0;
        _totalBytes = 0;
        PeakBytes = 0;
    }
}
