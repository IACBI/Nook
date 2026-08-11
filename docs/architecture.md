# Architecture

Written for anyone about to change the native code. English only; see the [README](../README.md) for user-facing documentation.

## Sampling loop

A single WinForms timer ticks once per second on the UI thread and drives everything. One tick is:

1. `PdhGpuMemoryReader.Read()` — one PDH collection, three counter arrays.
2. `GpuTelemetryEngine.GetTelemetry(adapterName)` — temperature, clock and load from the vendor driver.
3. Labels and overlay are updated from the values gathered above.

A second timer rebuilds the process list every ten seconds. That enumeration runs on a thread pool thread, because walking a few hundred processes is slow enough to show up as a stutter in the overlay.

## Where the numbers come from

| Value | Source |
|---|---|
| Per-process memory | PDH `\GPU Process Memory(*)\Dedicated Usage` and `\Shared Usage`, instance names carry the PID |
| Per-adapter memory | PDH `\GPU Adapter Memory(*)\Dedicated Usage` and `\Shared Usage`, instance names carry the LUID |
| Adapter load | PDH `\GPU Engine(*)\Utilization Percentage`, maximum across the adapter's engines |
| Adapter names | DXGI `IDXGIFactory1::EnumAdapters1`, falling back to `D3DKMTQueryAdapterInfo` |
| Temperature, core clock | NVIDIA NVML or AMD ADL, whichever loads |

Counters are added with `PdhAddEnglishCounterW`, so the counter paths above work on localised Windows installations as well.

Both memory pools are read because the dedicated one is frequently empty: integrated adapters have no dedicated memory, and on a hybrid laptop the discrete GPU keeps its allocations in the shared pool until something wakes it. A given adapter reports one instance per memory segment, so the values are summed per PID and per LUID.

## PDH buffer handling

`PdhGetFormattedCounterArrayW` writes an array of `PDH_FMT_COUNTERVALUE_ITEM_W` followed by the instance-name strings, all inside one caller-allocated block. The reader keeps that block alive between samples and only grows it, which keeps a per-second sample allocation-free apart from the parsed values.

Two consequences worth knowing:

- Instance names are read as `ReadOnlySpan<char>` straight out of the unmanaged buffer and parsed in place. Nothing may hold on to a name after the loop that produced it.
- `GpuReadResult` hands back the reader's own dictionaries rather than copies. They stay valid until the next `Read()`.

Every name pointer is bounds-checked against the buffer before it is dereferenced, and the struct layout is asserted in a static constructor — the code is x64-only and says so.

## DXGI without COM interop

`GpuAdapterCatalog` calls `EnumAdapters1` and `GetDesc1` through their vtable slots instead of declaring COM interfaces. It needs two methods on two interfaces, and this keeps the whole adapter catalogue to one file with no generated interop assembly. The slot numbers are derived from the `IUnknown` → `IDXGIObject` → `IDXGIFactory1` inheritance chain and are commented where they are declared; they are the one thing in the file that will break silently if edited carelessly.

Software adapters (the Microsoft Basic Render Driver) are filtered out by `DXGI_ADAPTER_FLAG_SOFTWARE`.

## Vendor telemetry

Neither `nvml.dll` nor `atiadlxx.dll` ships with Windows, so both are loaded with `NativeLibrary.TryLoad` and every entry point is treated as optional. NVML devices are matched to the selected adapter by product name; if no NVML device matches but the adapter is an NVIDIA one, the first device is used. When the selected adapter belongs to neither vendor — an Intel iGPU, say — the sample comes back empty and the overlay drops the temperature and clock rows rather than showing another card's readings.

## Overlay

`OverlayForm` is a `WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` window drawn with `UpdateLayeredWindow`, which is what gives it per-pixel alpha over a fullscreen game without a compositor round trip. It never takes focus: `WM_MOUSEACTIVATE` is answered with `MA_NOACTIVATE`, and in locked mode `WM_NCHITTEST` returns `HTTRANSPARENT` so clicks land on whatever is underneath.

Rows are measured and laid out from the row list, so adding a metric is a matter of adding a row; the window resizes itself to fit. Rendering is skipped when neither the text nor the lock state changed, which is the common case between samples.
