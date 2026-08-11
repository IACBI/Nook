using Nook;

namespace Nook.Tests;

public class ByteFormattingTests
{
    [Theory]
    [InlineData(0, "0.0 MB")]
    [InlineData(512, "0.0 MB")]
    [InlineData(1024 * 1024, "1.0 MB")]
    [InlineData(1024 * 1024 * 3 / 2, "1.5 MB")]
    [InlineData(512L * 1024 * 1024, "512.0 MB")]
    public void Megabytes_keep_one_decimal(double bytes, string expected) =>
        Assert.Equal(expected, MainForm.FormatBytes(bytes));

    [Theory]
    [InlineData(1024L * 1024 * 1024, "1.00 GB")]
    [InlineData(1024L * 1024 * 1024 * 3 / 2, "1.50 GB")]
    [InlineData(8L * 1024 * 1024 * 1024, "8.00 GB")]
    public void Gigabytes_take_over_at_the_boundary(double bytes, string expected) =>
        Assert.Equal(expected, MainForm.FormatBytes(bytes));

    /// <summary>
    /// The window runs with invariant globalization, so the decimal separator stays a dot
    /// even when Windows is set to a locale that uses a comma.
    /// </summary>
    [Fact]
    public void Decimal_separator_does_not_follow_the_system_locale() =>
        Assert.Contains('.', MainForm.FormatBytes(1024L * 1024 * 1024));
}

public class GpuNameTests
{
    [Theory]
    [InlineData("NVIDIA GeForce RTX 3050 Ti Laptop GPU", "RTX 3050 Ti")]
    [InlineData("NVIDIA GeForce RTX 4090", "RTX 4090")]
    [InlineData("NVIDIA RTX A2000", "RTX A2000")]
    [InlineData("AMD Radeon RX 6700 XT", "Radeon RX 6700 XT")]
    [InlineData("Intel(R) UHD Graphics", "UHD Graphics")]
    public void Vendor_boilerplate_is_trimmed(string reported, string expected) =>
        Assert.Equal(expected, OverlayForm.FormatShortGpuName(reported));

    [Fact]
    public void An_unfamiliar_name_is_left_alone() =>
        Assert.Equal("Microsoft Basic Render Driver", OverlayForm.FormatShortGpuName("Microsoft Basic Render Driver"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_name_falls_back(string reported) =>
        Assert.Equal("GPU", OverlayForm.FormatShortGpuName(reported));

    [Fact]
    public void A_null_name_falls_back() =>
        Assert.Equal("GPU", OverlayForm.FormatShortGpuName(null!));
}
