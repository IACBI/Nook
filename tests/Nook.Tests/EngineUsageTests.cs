using Nook;

namespace Nook.Tests;

/// <summary>
/// Adapter load is derived from per-process engine instances: Task Manager sums every process
/// on one physical engine, clamps that to 100, and reports the busiest engine of the adapter.
/// These tests pin both the instance-name parsing and that reduction.
/// </summary>
public class EngineUsageTests
{
    [Fact]
    public void TryGetEngineKey_reads_luid_phys_and_engine_from_a_real_name()
    {
        const string name = "pid_1760_luid_0x00000000_0x0000D1A5_phys_0_eng_3_engtype_3D";

        Assert.True(PdhGpuMemoryReader.TryGetEngineKey(name, out var key));
        Assert.Equal(new GpuEngineKey(new GpuLuid(0x0000D1A5u, 0x00000000u), 0, 3), key);
    }

    [Theory]
    [InlineData("pid_1760_luid_0x00000000_0x0000D1A5_eng_0_engtype_3D")]
    [InlineData("pid_1760_luid_0x00000000_0x0000D1A5_phys_0_engtype_3D")]
    [InlineData("pid_1760_luid_0x00000000_0x0000D1A5_phys_x_eng_0")]
    [InlineData("pid_1760_luid_0x00000000_0x0000D1A5_phys_0_eng_")]
    [InlineData("pid_1760_luid_0x00000000_0x0000D1A5_phys_-1_eng_0")]
    [InlineData("pid_1760_luid_0x00000000_0x0000D1A5_phys_0_eng_+2")]
    [InlineData("pid_1760_phys_0_eng_0")]
    public void TryGetEngineKey_rejects_names_with_missing_or_malformed_parts(string name)
    {
        Assert.False(PdhGpuMemoryReader.TryGetEngineKey(name, out var key));
        Assert.Equal(default, key);
    }

    [Fact]
    public void AggregateEngineUsage_sums_processes_on_the_same_engine()
    {
        var adapter = new GpuLuid(0x0000D1A5u, 0);
        var engines = new Dictionary<GpuEngineKey, double>
        {
            [new GpuEngineKey(adapter, 0, 0)] = 40 + 40,
        };
        var usage = new Dictionary<GpuLuid, double>();

        PdhGpuMemoryReader.AggregateEngineUsage(engines, usage);

        Assert.Equal(80, usage[adapter]);
    }

    [Fact]
    public void AggregateEngineUsage_clamps_a_saturated_engine_to_100()
    {
        var adapter = new GpuLuid(0x0000D1A5u, 0);
        var engines = new Dictionary<GpuEngineKey, double>
        {
            [new GpuEngineKey(adapter, 0, 0)] = 70 + 50,
        };
        var usage = new Dictionary<GpuLuid, double>();

        PdhGpuMemoryReader.AggregateEngineUsage(engines, usage);

        Assert.Equal(100, usage[adapter]);
    }

    [Fact]
    public void AggregateEngineUsage_takes_the_busiest_engine_of_an_adapter()
    {
        var adapter = new GpuLuid(0x0000D1A5u, 0);
        var engines = new Dictionary<GpuEngineKey, double>
        {
            [new GpuEngineKey(adapter, 0, 0)] = 30,
            [new GpuEngineKey(adapter, 0, 1)] = 60,
        };
        var usage = new Dictionary<GpuLuid, double>();

        PdhGpuMemoryReader.AggregateEngineUsage(engines, usage);

        Assert.Equal(60, usage[adapter]);
    }

    [Fact]
    public void AggregateEngineUsage_keeps_adapters_separate()
    {
        var first = new GpuLuid(0x0000D1A5u, 0);
        var second = new GpuLuid(0x0000F633u, 0);
        var engines = new Dictionary<GpuEngineKey, double>
        {
            [new GpuEngineKey(first, 0, 0)] = 25,
            [new GpuEngineKey(second, 0, 0)] = 90,
        };
        var usage = new Dictionary<GpuLuid, double>();

        PdhGpuMemoryReader.AggregateEngineUsage(engines, usage);

        Assert.Equal(2, usage.Count);
        Assert.Equal(25, usage[first]);
        Assert.Equal(90, usage[second]);
    }

    [Fact]
    public void AggregateEngineUsage_clears_previous_results()
    {
        var stale = new GpuLuid(0x0000F633u, 0);
        var usage = new Dictionary<GpuLuid, double> { [stale] = 50 };

        PdhGpuMemoryReader.AggregateEngineUsage(new Dictionary<GpuEngineKey, double>(), usage);

        Assert.Empty(usage);
    }
}
