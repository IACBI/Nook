using Nook;

namespace Nook.Tests;

/// <summary>
/// PDH hands every sample back keyed by an instance name such as
/// <c>pid_1760_luid_0x00000000_0x0000F633_phys_0</c>. If the parsing below drifts, nothing
/// crashes — the numbers just land on the wrong process or the wrong adapter — so the exact
/// strings Windows produces are pinned here.
/// </summary>
public class CounterInstanceNameTests
{
    [Theory]
    [InlineData("pid_1760_luid_0x00000000_0x0000F633_phys_0", 1760)]
    [InlineData("pid_1760_luid_0x00000000_0x0000F633_phys_0_eng_0_engtype_3D", 1760)]
    [InlineData("pid_0_luid_0x00000000_0x0000F633_phys_0", 0)]
    [InlineData("PID_42_luid_0x00000000_0x0000F633_phys_0", 42)]
    public void TryGetProcessId_reads_the_pid(string instanceName, int expected)
    {
        Assert.True(PdhGpuMemoryReader.TryGetProcessId(instanceName, out var processId));
        Assert.Equal(expected, processId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("luid_0x00000000_0x0000F633_phys_0")]
    [InlineData("pid_abc_luid_0x00000000_0x0000F633")]
    [InlineData("pid__luid_0x00000000_0x0000F633")]
    [InlineData("pid_-5_luid_0x00000000_0x0000F633")]
    [InlineData("pid_99999999999_luid_0x00000000_0x0000F633")]
    [InlineData("pid_1760")]
    public void TryGetProcessId_rejects_anything_else(string instanceName)
    {
        Assert.False(PdhGpuMemoryReader.TryGetProcessId(instanceName, out var processId));
        Assert.Equal(0, processId);
    }

    [Theory]
    [InlineData("luid_0x00000000_0x0000F633_phys_0", 0x0000F633u, 0x00000000u)]
    [InlineData("pid_1760_luid_0x00000000_0x0000F633_phys_0", 0x0000F633u, 0x00000000u)]
    [InlineData("pid_1760_luid_0x00000000_0x0000F633_phys_0_eng_0_engtype_3D", 0x0000F633u, 0x00000000u)]
    [InlineData("luid_0x0000ABCD_0x12345678_phys_0", 0x12345678u, 0x0000ABCDu)]
    [InlineData("LUID_0x00000000_0x0000F633_phys_0", 0x0000F633u, 0x00000000u)]
    public void TryGetLuid_reads_both_halves(string instanceName, uint expectedLow, uint expectedHigh)
    {
        Assert.True(PdhGpuMemoryReader.TryGetLuid(instanceName, out var luid));
        Assert.Equal(new GpuLuid(expectedLow, expectedHigh), luid);
    }

    [Fact]
    public void TryGetLuid_accepts_a_name_that_ends_at_the_low_half()
    {
        Assert.True(PdhGpuMemoryReader.TryGetLuid("luid_0x00000000_0x0000F633", out var luid));
        Assert.Equal(new GpuLuid(0x0000F633u, 0x00000000u), luid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pid_1760")]
    [InlineData("luid_0x00000000")]
    [InlineData("luid_0x0000_0x")]
    [InlineData("luid_0xZZZZZZZZ_0x0000F633_phys_0")]
    [InlineData("luid_0x00000000_0xZZZZZZZZ_phys_0")]
    public void TryGetLuid_rejects_anything_else(string instanceName)
    {
        Assert.False(PdhGpuMemoryReader.TryGetLuid(instanceName, out var luid));
        Assert.Equal(default, luid);
    }

    /// <summary>
    /// The adapter list treats a name starting with "LUID 0x" as "Windows could not name this
    /// adapter" and skips it, so the two have to keep agreeing on the prefix.
    /// </summary>
    [Fact]
    public void GpuLuid_falls_back_to_a_recognisable_string()
    {
        var text = new GpuLuid(0x0000F633u, 0x00000000u).ToString();

        Assert.Equal("LUID 0x00000000_0x0000F633", text);
        Assert.StartsWith("LUID 0x", text, StringComparison.OrdinalIgnoreCase);
    }
}
