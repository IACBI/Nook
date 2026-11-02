using Nook;

namespace Nook.Tests;

/// <summary>
/// After a driver reset the vendor handles never answer again, so Nook reopens the libraries.
/// These tests pin when that happens: only for an adapter that answered before, only after a run
/// of empty samples, and never more often than the cool-down allows.
/// </summary>
public class TelemetryRecoveryTests
{
    private static readonly GpuLuid Nvidia = new(0xFBDC, 0);
    private static readonly GpuLuid Intel = new(0x1234, 0);
    private static readonly GpuTelemetrySample Reading = new(61, 1755, 12);
    private static readonly GpuTelemetrySample Empty = default;

    [Fact]
    public void An_adapter_that_never_answered_is_never_reopened()
    {
        var recovery = new TelemetryRecovery(emptySamplesBeforeReopen: 3, minSamplesBetweenReopens: 1, maxSamplesBetweenReopens: 1);

        for (var sample = 0; sample < 100; sample++)
        {
            Assert.False(recovery.ShouldReopen(Intel, Empty));
        }
    }

    [Fact]
    public void Lost_readings_reopen_after_the_threshold()
    {
        var recovery = new TelemetryRecovery(emptySamplesBeforeReopen: 3, minSamplesBetweenReopens: 1, maxSamplesBetweenReopens: 1);
        Assert.False(recovery.ShouldReopen(Nvidia, Reading));

        Assert.False(recovery.ShouldReopen(Nvidia, Empty));
        Assert.False(recovery.ShouldReopen(Nvidia, Empty));
        Assert.True(recovery.ShouldReopen(Nvidia, Empty));
    }

    [Fact]
    public void A_reading_in_between_restarts_the_count()
    {
        var recovery = new TelemetryRecovery(emptySamplesBeforeReopen: 3, minSamplesBetweenReopens: 1, maxSamplesBetweenReopens: 1);
        recovery.ShouldReopen(Nvidia, Reading);

        Assert.False(recovery.ShouldReopen(Nvidia, Empty));
        Assert.False(recovery.ShouldReopen(Nvidia, Empty));
        Assert.False(recovery.ShouldReopen(Nvidia, Reading));
        Assert.False(recovery.ShouldReopen(Nvidia, Empty));
        Assert.False(recovery.ShouldReopen(Nvidia, Empty));
        Assert.True(recovery.ShouldReopen(Nvidia, Empty));
    }

    [Fact]
    public void A_partial_reading_counts_as_an_answer()
    {
        var recovery = new TelemetryRecovery(emptySamplesBeforeReopen: 1, minSamplesBetweenReopens: 1, maxSamplesBetweenReopens: 1);

        Assert.False(recovery.ShouldReopen(Nvidia, new GpuTelemetrySample(null, null, 5)));
        Assert.True(recovery.ShouldReopen(Nvidia, Empty));
    }

    [Fact]
    public void Switching_to_an_adapter_without_sensors_does_not_reopen()
    {
        var recovery = new TelemetryRecovery(emptySamplesBeforeReopen: 2, minSamplesBetweenReopens: 1, maxSamplesBetweenReopens: 1);
        recovery.ShouldReopen(Nvidia, Reading);
        recovery.ShouldReopen(Nvidia, Empty);

        for (var sample = 0; sample < 10; sample++)
        {
            Assert.False(recovery.ShouldReopen(Intel, Empty));
        }
    }

    [Fact]
    public void Reopens_that_bring_nothing_back_are_spaced_further_apart_up_to_the_cap()
    {
        var recovery = new TelemetryRecovery(emptySamplesBeforeReopen: 1, minSamplesBetweenReopens: 5, maxSamplesBetweenReopens: 20);
        recovery.ShouldReopen(Nvidia, Reading);

        Assert.True(recovery.ShouldReopen(Nvidia, Empty));
        Assert.Equal([5, 10, 20, 20], Enumerable.Range(0, 4).Select(_ => SamplesUntilReopen(recovery)));
    }

    [Fact]
    public void Readings_coming_back_reset_the_interval()
    {
        var recovery = new TelemetryRecovery(emptySamplesBeforeReopen: 1, minSamplesBetweenReopens: 5, maxSamplesBetweenReopens: 20);
        recovery.ShouldReopen(Nvidia, Reading);
        recovery.ShouldReopen(Nvidia, Empty);
        SamplesUntilReopen(recovery);
        SamplesUntilReopen(recovery);

        for (var sample = 0; sample < 30; sample++)
        {
            recovery.ShouldReopen(Nvidia, Reading);
        }

        Assert.True(recovery.ShouldReopen(Nvidia, Empty));
        Assert.Equal(5, SamplesUntilReopen(recovery));
    }

    [Fact]
    public void Invalid_thresholds_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TelemetryRecovery(0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TelemetryRecovery(1, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TelemetryRecovery(1, 5, 4));
    }

    /// <summary>Feeds empty samples until a reopen is requested and returns how many it took.</summary>
    private static int SamplesUntilReopen(TelemetryRecovery recovery)
    {
        for (var samples = 1; samples <= 1000; samples++)
        {
            if (recovery.ShouldReopen(Nvidia, Empty))
            {
                return samples;
            }
        }

        throw new InvalidOperationException("No reopen within 1000 samples.");
    }
}
