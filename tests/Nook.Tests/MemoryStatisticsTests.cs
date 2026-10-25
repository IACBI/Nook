using Nook;

namespace Nook.Tests;

/// <summary>Peak and average are what the Process tab shows for a whole session, so a wrong reset is visible for minutes.</summary>
public class MemoryStatisticsTests
{
    [Fact]
    public void Starts_empty()
    {
        var statistics = new MemoryStatistics();

        Assert.False(statistics.HasSamples);
        Assert.Equal(0, statistics.PeakBytes);
        Assert.Equal(0, statistics.AverageBytes);
    }

    [Fact]
    public void Tracks_the_peak_and_the_mean()
    {
        var statistics = new MemoryStatistics();

        statistics.Add(100);
        statistics.Add(400);
        statistics.Add(250);

        Assert.True(statistics.HasSamples);
        Assert.Equal(400, statistics.PeakBytes);
        Assert.Equal(250, statistics.AverageBytes);
    }

    [Fact]
    public void A_zero_sample_counts_towards_the_mean()
    {
        var statistics = new MemoryStatistics();

        statistics.Add(300);
        statistics.Add(0);

        Assert.Equal(150, statistics.AverageBytes);
        Assert.Equal(300, statistics.PeakBytes);
    }

    [Fact]
    public void Reset_forgets_everything()
    {
        var statistics = new MemoryStatistics();
        statistics.Add(512);

        statistics.Reset();
        statistics.Add(64);

        Assert.Equal(64, statistics.PeakBytes);
        Assert.Equal(64, statistics.AverageBytes);
    }
}
