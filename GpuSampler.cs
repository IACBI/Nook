using System.Diagnostics;

namespace Nook;

/// <summary>
/// What the UI wants measured. The UI replaces it whole and never mutates it, so the sampler
/// thread always sees a GPU and a process that belong together.
/// </summary>
internal sealed record SampleRequest(GpuLuid? Gpu, string? GpuName, int? Pid, string? ProcessName)
{
    public static readonly SampleRequest None = new(null, null, null, null);
}

/// <summary>
/// One sample, copied out of the native readers. The PDH reader overwrites its dictionaries on
/// every read, so nothing it returns may cross to the UI thread without being copied first.
/// </summary>
internal sealed record GpuSnapshot(
    SampleRequest Request,
    bool IsAvailable,
    string? Error,
    Exception? ErrorException,
    IReadOnlyDictionary<int, long> ProcessDedicatedBytes,
    IReadOnlyDictionary<int, long> ProcessSharedBytes,
    IReadOnlyDictionary<GpuLuid, long> AdapterDedicatedBytes,
    IReadOnlyDictionary<GpuLuid, long> AdapterSharedBytes,
    IReadOnlyDictionary<GpuLuid, double> AdapterUsagePercent,
    IReadOnlyDictionary<GpuLuid, string> AdapterNames,
    GpuTelemetrySample Telemetry,
    ProcessLookup Process)
{
    /// <summary>
    /// A sample that produced no counter data. The process lookup is kept only when it really ran:
    /// a sample that failed before reaching it says nothing about whether the process exited.
    /// </summary>
    public static GpuSnapshot Unavailable(SampleRequest request, string error, Exception? exception, ProcessLookup process = default) =>
        new(request, false, error, exception,
            new Dictionary<int, long>(), new Dictionary<int, long>(),
            new Dictionary<GpuLuid, long>(), new Dictionary<GpuLuid, long>(),
            new Dictionary<GpuLuid, double>(), new Dictionary<GpuLuid, string>(),
            default, process);

    /// <summary>Copies an available read so the reader can be sampled again while the UI still holds this snapshot.</summary>
    public static GpuSnapshot FromRead(
        SampleRequest request,
        GpuReadResult read,
        IReadOnlyDictionary<GpuLuid, string> adapterNames,
        GpuTelemetrySample telemetry,
        ProcessLookup process) =>
        new(request, true, null, null,
            new Dictionary<int, long>(read.ProcessDedicatedBytes!),
            new Dictionary<int, long>(read.ProcessSharedBytes!),
            new Dictionary<GpuLuid, long>(read.AdapterDedicatedBytes!),
            new Dictionary<GpuLuid, long>(read.AdapterSharedBytes!),
            new Dictionary<GpuLuid, double>(read.AdapterUsagePercent!),
            new Dictionary<GpuLuid, string>(adapterNames),
            telemetry, process);
}

/// <summary>
/// The monitored process as the sampler found it. <see cref="Checked"/> separates "looked and it is
/// gone" (<see cref="Live"/> null) from "never looked", which the default value means.
/// </summary>
internal readonly record struct ProcessLookup(bool Checked, (string Name, DateTime? StartTime)? Live)
{
    public static ProcessLookup Of((string Name, DateTime? StartTime)? live) => new(true, live);
}

/// <summary>Produces one sample. Created, used and disposed on the sampler thread only.</summary>
internal interface ISampleSource : IDisposable
{
    GpuSnapshot Sample(SampleRequest request);
}

/// <summary>
/// Samples on a dedicated background thread once per interval. A driver call that hangs cannot
/// be cancelled, so it is kept off the UI thread: the window stays responsive and can watch
/// <see cref="BusyFor"/> to tell the user that the driver stopped answering.
/// </summary>
internal sealed class GpuSampler : IDisposable
{
    private static readonly TimeSpan SlowSampleThreshold = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SlowSampleLogInterval = TimeSpan.FromMinutes(1);

    private readonly Func<ISampleSource> _createSource;
    private readonly Action<GpuSnapshot> _publish;
    private readonly Action<string>? _warn;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;
    private SampleRequest _request = SampleRequest.None;
    private long _busySince;
    private long _lastSlowWarning;

    /// <param name="createSource">Runs on the sampler thread, so every native handle the source opens belongs to it.</param>
    /// <param name="publish">Runs on the sampler thread; it must hand the snapshot to the UI thread itself.</param>
    /// <param name="interval">Time from the start of one sample to the start of the next.</param>
    /// <param name="warn">Receives slow-sample and publish-failure warnings.</param>
    public GpuSampler(Func<ISampleSource> createSource, Action<GpuSnapshot> publish, TimeSpan interval, Action<string>? warn = null)
    {
        _createSource = createSource;
        _publish = publish;
        _interval = interval;
        _warn = warn;
        // A background thread: a driver call that never returns must not keep the process alive after exit.
        _thread = new Thread(Run) { IsBackground = true, Name = "Nook sampler" };
    }

    /// <summary>The GPU and process to measure from the next sample on.</summary>
    public SampleRequest Request
    {
        get => Volatile.Read(ref _request);
        set => Volatile.Write(ref _request, value);
    }

    /// <summary>How long the sample in progress has been running, or zero between samples.</summary>
    public TimeSpan BusyFor
    {
        get
        {
            var since = Interlocked.Read(ref _busySince);
            return since == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(since);
        }
    }

    public void Start() => _thread.Start();

    /// <summary>
    /// Asks the thread to stop and waits up to <paramref name="timeout"/>. Returns false when a
    /// driver call is still running; the thread then exits, and releases its source, when it returns.
    /// </summary>
    public bool Stop(TimeSpan timeout)
    {
        _stop.Cancel();
        return !_thread.IsAlive || _thread.Join(timeout);
    }

    public void Dispose()
    {
        if (Stop(TimeSpan.FromSeconds(2)))
        {
            // Only once the thread is gone: it may still be waiting on this token's handle.
            _stop.Dispose();
        }
    }

    private void Run()
    {
        ISampleSource? source = null;
        try
        {
            // Opening NVML or PDH can hang as well, so creation counts as busy time.
            Interlocked.Exchange(ref _busySince, Stopwatch.GetTimestamp());
            try
            {
                source = _createSource();
            }
            catch (Exception ex)
            {
                Publish(GpuSnapshot.Unavailable(Request, $"GPU monitoring could not start: {ex.Message}", ex));
                return;
            }
            finally
            {
                Interlocked.Exchange(ref _busySince, 0);
            }

            while (!_stop.IsCancellationRequested)
            {
                var request = Request;
                var started = Stopwatch.GetTimestamp();
                Interlocked.Exchange(ref _busySince, started);
                GpuSnapshot snapshot;
                try
                {
                    snapshot = source.Sample(request);
                }
                catch (Exception ex)
                {
                    snapshot = GpuSnapshot.Unavailable(request, $"GPU sample failed: {ex.Message}", ex);
                }
                finally
                {
                    Interlocked.Exchange(ref _busySince, 0);
                }

                var elapsed = Stopwatch.GetElapsedTime(started);
                ReportIfSlow(elapsed);
                if (_stop.IsCancellationRequested)
                {
                    break;
                }

                Publish(snapshot);
                var wait = _interval - elapsed;
                if (wait > TimeSpan.Zero)
                {
                    _stop.Token.WaitHandle.WaitOne(wait);
                }
            }
        }
        finally
        {
            source?.Dispose();
        }
    }

    private void Publish(GpuSnapshot snapshot)
    {
        try
        {
            _publish(snapshot);
        }
        catch (Exception ex)
        {
            _warn?.Invoke($"A GPU sample could not be handed to the window: {ex.Message}");
        }
    }

    private void ReportIfSlow(TimeSpan elapsed)
    {
        if (elapsed < SlowSampleThreshold || _warn is null)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (_lastSlowWarning != 0 && Stopwatch.GetElapsedTime(_lastSlowWarning, now) < SlowSampleLogInterval)
        {
            return;
        }

        _lastSlowWarning = now;
        _warn($"A GPU sample took {elapsed.TotalMilliseconds:0} ms; the driver is answering slowly.");
    }
}
