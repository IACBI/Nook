using Nook;

namespace Nook.Tests;

/// <summary>
/// Hand-declared native layouts fail silently: a struct that is too short lets the driver
/// write past it, and a wrong vtable slot calls a different method that happens to share a
/// signature shape. These tests pin both against the vendor and SDK headers.
/// </summary>
public class NativeLayoutTests
{
    [Fact]
    public void AdlPerformanceStatus_matches_the_adl_sdk_size()
    {
        Assert.Equal(72, GpuTelemetryEngine.AdlPerformanceStatusNativeSize);
        Assert.Equal(GpuTelemetryEngine.AdlPerformanceStatusNativeSize, GpuTelemetryEngine.AdlPerformanceStatusManagedSize);
    }

    [Fact]
    public void Dxgi_vtable_slots_match_dxgi_h()
    {
        // dxgi.h: IUnknown 0-2, IDXGIObject 3-6, IDXGIFactory 7-11, IDXGIFactory1::EnumAdapters1 12.
        Assert.Equal(12, GpuAdapterCatalog.EnumAdapters1VtableSlot);
        // dxgi.h: IUnknown 0-2, IDXGIObject 3-6, IDXGIAdapter 7-9, IDXGIAdapter1::GetDesc1 10.
        Assert.Equal(10, GpuAdapterCatalog.GetDesc1VtableSlot);
    }

    [Fact]
    public void Dxgi_enumeration_returns_readable_adapter_names()
    {
        // The Basic Render Driver is always enumerated, so this holds on a headless CI runner too.
        var adapters = GpuAdapterCatalog.EnumerateAdapters(includeSoftware: true);

        Assert.NotEmpty(adapters);
        Assert.Equal(adapters.Count, adapters.Select(adapter => adapter.Luid).Distinct().Count());
        foreach (var adapter in adapters)
        {
            // Ansi-marshalled WCHAR text comes back as CJK-range garbage, so stay within Latin-1.
            Assert.All(adapter.Name, character => Assert.InRange(character, ' ', 'ÿ'));
        }
    }
}
