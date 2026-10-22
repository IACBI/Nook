using Nook;

namespace Nook.Tests;

/// <summary>
/// Instance names come straight out of unmanaged PDH memory. The reader must find the NUL
/// terminator without scanning past the region it was handed, and must reject names longer
/// than the limit instead of trusting the buffer to end where it should.
/// </summary>
public class InstanceNameBoundsTests
{
    [Fact]
    public void TryReadInstanceName_returns_the_name_before_the_terminator()
    {
        const string name = "pid_1760_luid_0x00000000_0x0000D1A5_phys_0";
        var region = (name + "\0" + "garbage\u1234\u5678").AsSpan();

        Assert.True(PdhGpuMemoryReader.TryReadInstanceName(region, out var result));
        Assert.Equal(name, result.ToString());
    }

    [Fact]
    public void TryReadInstanceName_rejects_a_region_with_no_terminator()
    {
        var region = new string('a', 64).AsSpan();

        Assert.False(PdhGpuMemoryReader.TryReadInstanceName(region, out var result));
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void TryReadInstanceName_rejects_an_unterminated_region_longer_than_the_limit()
    {
        var region = new string('a', PdhGpuMemoryReader.MaxInstanceNameCharacters * 2).AsSpan();

        Assert.False(PdhGpuMemoryReader.TryReadInstanceName(region, out var result));
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void TryReadInstanceName_rejects_an_empty_name()
    {
        var region = "\0pid_1760".AsSpan();

        Assert.False(PdhGpuMemoryReader.TryReadInstanceName(region, out var result));
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void TryReadInstanceName_rejects_an_empty_region()
    {
        Assert.False(PdhGpuMemoryReader.TryReadInstanceName(ReadOnlySpan<char>.Empty, out var result));
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void TryReadInstanceName_accepts_a_name_at_the_limit()
    {
        var name = new string('a', PdhGpuMemoryReader.MaxInstanceNameCharacters);
        var region = (name + "\0").AsSpan();

        Assert.True(PdhGpuMemoryReader.TryReadInstanceName(region, out var result));
        Assert.Equal(name, result.ToString());
    }

    [Fact]
    public void TryReadInstanceName_rejects_a_name_one_character_over_the_limit()
    {
        var region = (new string('a', PdhGpuMemoryReader.MaxInstanceNameCharacters + 1) + "\0").AsSpan();

        Assert.False(PdhGpuMemoryReader.TryReadInstanceName(region, out var result));
        Assert.True(result.IsEmpty);
    }
}
