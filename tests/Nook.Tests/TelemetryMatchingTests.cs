using Nook;

namespace Nook.Tests;

/// <summary>
/// Vendor readings must come from the card the user selected. A reading from another card is
/// worse than none, so each rule here fails closed: a missing or ambiguous match yields -1.
/// </summary>
public class TelemetryMatchingTests
{
    private static readonly GpuPciAddress Discrete = new(1, 0, 0);
    private static readonly GpuPciAddress Integrated = new(0, 2, 0);
    private const string NvidiaName = "NVIDIA GeForce RTX 3050 Ti Laptop GPU";

    [Fact]
    public void Pci_location_wins_over_a_matching_name()
    {
        var devices = new (GpuPciAddress? Address, string Name)[] { (Integrated, NvidiaName), (Discrete, NvidiaName) };

        Assert.Equal(1, GpuTelemetryEngine.ResolveDeviceIndex(devices, Discrete, NvidiaName));
    }

    [Fact]
    public void Known_pci_location_with_no_device_there_is_never_matched_by_name()
    {
        var devices = new (GpuPciAddress? Address, string Name)[] { (Integrated, NvidiaName) };

        Assert.Equal(-1, GpuTelemetryEngine.ResolveDeviceIndex(devices, Discrete, NvidiaName));
    }

    [Fact]
    public void Unknown_pci_location_takes_the_only_device_with_that_name()
    {
        var devices = new (GpuPciAddress? Address, string Name)[] { (null, "Intel(R) UHD Graphics"), (null, NvidiaName) };

        Assert.Equal(1, GpuTelemetryEngine.ResolveDeviceIndex(devices, null, NvidiaName));
    }

    [Fact]
    public void Unknown_pci_location_refuses_two_devices_with_the_same_name()
    {
        var devices = new (GpuPciAddress? Address, string Name)[] { (Integrated, NvidiaName), (Discrete, NvidiaName) };

        Assert.Equal(-1, GpuTelemetryEngine.ResolveDeviceIndex(devices, null, NvidiaName));
    }

    [Fact]
    public void Unknown_pci_location_with_no_name_match_does_not_fall_back_to_the_first_device()
    {
        var devices = new (GpuPciAddress? Address, string Name)[] { (Discrete, NvidiaName) };

        Assert.Equal(-1, GpuTelemetryEngine.ResolveDeviceIndex(devices, null, "Intel(R) UHD Graphics"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_adapter_name_matches_nothing_without_a_pci_location(string? adapterName)
    {
        var devices = new (GpuPciAddress? Address, string Name)[] { (Discrete, string.Empty), (null, string.Empty) };

        Assert.Equal(-1, GpuTelemetryEngine.ResolveDeviceIndex(devices, null, adapterName));
    }

    [Fact]
    public void Adl_pci_location_selects_the_matching_physical_adapter()
    {
        var adapters = new[] { new AdlAdapter(0, Integrated), new AdlAdapter(4, Discrete) };

        Assert.Equal(4, GpuTelemetryEngine.ResolveAdlAdapterIndex(adapters, Discrete, "AMD Radeon RX 6600"));
        Assert.Equal(-1, GpuTelemetryEngine.ResolveAdlAdapterIndex(adapters, new GpuPciAddress(7, 0, 0), "AMD Radeon RX 6600"));
    }

    [Fact]
    public void Adl_without_a_pci_location_uses_the_index_only_for_a_single_amd_adapter()
    {
        var single = new[] { new AdlAdapter(2, Discrete) };
        var pair = new[] { new AdlAdapter(0, Integrated), new AdlAdapter(2, Discrete) };

        Assert.Equal(2, GpuTelemetryEngine.ResolveAdlAdapterIndex(single, null, "AMD Radeon RX 6600"));
        Assert.Equal(2, GpuTelemetryEngine.ResolveAdlAdapterIndex(single, null, "Radeon Graphics"));
        Assert.Equal(-1, GpuTelemetryEngine.ResolveAdlAdapterIndex(single, null, NvidiaName));
        Assert.Equal(-1, GpuTelemetryEngine.ResolveAdlAdapterIndex(pair, null, "AMD Radeon RX 6600"));
        Assert.Equal(-1, GpuTelemetryEngine.ResolveAdlAdapterIndex(single, null, null));
        Assert.Equal(-1, GpuTelemetryEngine.ResolveAdlAdapterIndex(single, null, "   "));
    }

    [Theory]
    [InlineData("00000000:01:00.0", 0u)]
    [InlineData("00000000:65:00.1", 1u)]
    [InlineData("00000000:65:00.7", 7u)]
    [InlineData("garbage", 0u)]
    [InlineData("", 0u)]
    [InlineData("00000000:01:00.", 0u)]
    [InlineData("00000000:01:00.x", 0u)]
    [InlineData("00000000:01:00.8", 0u)]
    public void Nvml_bus_id_yields_the_pci_function(string busId, uint expected)
    {
        Assert.Equal(expected, GpuTelemetryEngine.ParsePciFunction(busId));
    }
}
