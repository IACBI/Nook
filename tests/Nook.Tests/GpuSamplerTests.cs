using System.Collections.Concurrent;
using Nook;

namespace Nook.Tests;

/// <summary>
/// The sampler exists so that a driver call which never returns cannot freeze the window. These
/// tests drive it with a fake source that can be made to hang, and pin the threading contract:
/// one owner thread for the native source, a visible busy time while stuck, and a bounded stop.
/// </summary>
public class GpuSamplerTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [Fact]
    public void Samples_repeatedly_with_the_latest_request()
    {
        var published = new BlockingCollection<GpuSnapshot>();
        using var source = new FakeSource();
        using var sampler = new GpuSampler(() => source, published.Add, Interval);
        var request = new SampleRequest(new GpuLuid(0xFBDC, 0), "NVIDIA GeForce RTX 3050 Ti Laptop GPU", 1760, "game");

        sampler.Start();
        Assert.True(published.TryTake(out _, Patience));
        sampler.Request = request;

        Assert.True(WaitFor(published, snapshot => snapshot.Request == request));
    }

    [Fact]
    public void The_source_lives_on_one_thread_that_is_not_the_callers()
    {
        var published = new BlockingCollection<GpuSnapshot>();
        var source = new FakeSource();
        var sampler = new GpuSampler(() => source.Created(), published.Add, Interval);

        sampler.Start();
        Assert.True(published.TryTake(out _, Patience));
        Assert.True(published.TryTake(out _, Patience));
        Assert.True(sampler.Stop(Patience));

        var callerThread = Environment.CurrentManagedThreadId;
        Assert.True(source.Disposed.Wait(Patience));
        var threads = source.Threads.Distinct().ToList();
        Assert.Single(threads);
        Assert.NotEqual(callerThread, threads[0]);
    }

    [Fact]
    public void A_throwing_sample_is_published_as_unavailable_and_sampling_continues()
    {
        var published = new BlockingCollection<GpuSnapshot>();
        using var source = new FakeSource { ThrowOnSample = 1 };
        using var sampler = new GpuSampler(() => source, published.Add, Interval);

        sampler.Start();

        Assert.True(published.TryTake(out var failed, Patience));
        Assert.False(failed!.IsAvailable);
        Assert.Contains("boom", failed.Error);
        Assert.IsType<InvalidOperationException>(failed.ErrorException);
        Assert.True(published.TryTake(out var next, Patience));
        Assert.True(next!.IsAvailable);
    }

    [Fact]
    public void A_hung_sample_shows_as_busy_and_stop_does_not_wait_for_it()
    {
        var published = new BlockingCollection<GpuSnapshot>();
        var source = new FakeSource();
        var sampler = new GpuSampler(() => source, published.Add, Interval);
        source.Hang.Reset();

        sampler.Start();
        Assert.True(source.Entered.Wait(Patience));
        Thread.Sleep(200);

        Assert.True(sampler.BusyFor >= TimeSpan.FromMilliseconds(150));
        var started = DateTime.UtcNow;
        Assert.False(sampler.Stop(TimeSpan.FromMilliseconds(100)));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2));
        Assert.Empty(published);

        // Once the driver answers, the thread notices the stop request and releases the source.
        source.Hang.Set();
        Assert.True(source.Disposed.Wait(Patience));
        Assert.Empty(published);
    }

    [Fact]
    public void Busy_time_is_zero_between_samples()
    {
        var published = new BlockingCollection<GpuSnapshot>();
        using var source = new FakeSource();
        using var sampler = new GpuSampler(() => source, published.Add, TimeSpan.FromSeconds(10));

        sampler.Start();
        Assert.True(published.TryTake(out _, Patience));

        Assert.Equal(TimeSpan.Zero, sampler.BusyFor);
    }

    [Fact]
    public void A_source_that_cannot_be_created_is_reported_once()
    {
        var published = new BlockingCollection<GpuSnapshot>();
        using var sampler = new GpuSampler(() => throw new InvalidOperationException("no driver"), published.Add, Interval);

        sampler.Start();

        Assert.True(published.TryTake(out var snapshot, Patience));
        Assert.False(snapshot!.IsAvailable);
        Assert.Contains("no driver", snapshot.Error);
        Assert.True(sampler.Stop(Patience));
        Assert.Empty(published);
    }

    [Fact]
    public void A_failing_publish_does_not_stop_sampling()
    {
        var calls = 0;
        var warnings = new ConcurrentQueue<string>();
        var published = new BlockingCollection<GpuSnapshot>();
        using var source = new FakeSource();
        using var sampler = new GpuSampler(
            () => source,
            snapshot =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    throw new InvalidOperationException("window gone");
                }

                published.Add(snapshot);
            },
            Interval,
            warnings.Enqueue);

        sampler.Start();

        Assert.True(published.TryTake(out _, Patience));
        Assert.Contains(warnings, warning => warning.Contains("window gone"));
    }

    [Fact]
    public void Stop_before_start_returns_immediately()
    {
        using var source = new FakeSource();
        var sampler = new GpuSampler(() => source, _ => { }, Interval);

        Assert.True(sampler.Stop(TimeSpan.Zero));
        sampler.Dispose();
    }

    [Fact]
    public void A_snapshot_does_not_change_when_the_reader_reuses_its_dictionaries()
    {
        var luid = new GpuLuid(0xFBDC, 0);
        var processDedicated = new Dictionary<int, long> { [1760] = 512 };
        var adapterUsage = new Dictionary<GpuLuid, double> { [luid] = 40 };
        var read = GpuReadResult.Available(processDedicated, new Dictionary<int, long>(), new Dictionary<GpuLuid, long>(), new Dictionary<GpuLuid, long>(), adapterUsage);
        var names = new Dictionary<GpuLuid, string> { [luid] = "NVIDIA" };

        var snapshot = GpuSnapshot.FromRead(SampleRequest.None, read, names, default, null);
        processDedicated[1760] = 4096;
        processDedicated[42] = 1;
        adapterUsage.Clear();
        names.Clear();

        Assert.Equal(512, snapshot.ProcessDedicatedBytes[1760]);
        Assert.False(snapshot.ProcessDedicatedBytes.ContainsKey(42));
        Assert.Equal(40, snapshot.AdapterUsagePercent[luid]);
        Assert.Equal("NVIDIA", snapshot.AdapterNames[luid]);
    }

    private static bool WaitFor(BlockingCollection<GpuSnapshot> published, Func<GpuSnapshot, bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            if (published.TryTake(out var snapshot, deadline - DateTime.UtcNow) && condition(snapshot))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class FakeSource : ISampleSource
    {
        private int _samples;

        public ManualResetEventSlim Hang { get; } = new(initialState: true);
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Disposed { get; } = new();
        public ConcurrentQueue<int> Threads { get; } = new();
        public int ThrowOnSample { get; init; }

        public FakeSource Created()
        {
            Threads.Enqueue(Environment.CurrentManagedThreadId);
            return this;
        }

        public GpuSnapshot Sample(SampleRequest request)
        {
            Threads.Enqueue(Environment.CurrentManagedThreadId);
            Entered.Set();
            Hang.Wait();
            if (Interlocked.Increment(ref _samples) == ThrowOnSample)
            {
                throw new InvalidOperationException("boom");
            }

            var empty = GpuReadResult.Available(new Dictionary<int, long>(), new Dictionary<int, long>(), new Dictionary<GpuLuid, long>(), new Dictionary<GpuLuid, long>(), new Dictionary<GpuLuid, double>());
            return GpuSnapshot.FromRead(request, empty, new Dictionary<GpuLuid, string>(), default, null);
        }

        public void Dispose()
        {
            Threads.Enqueue(Environment.CurrentManagedThreadId);
            Disposed.Set();
        }
    }
}
