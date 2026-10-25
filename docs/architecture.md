# Architecture

Written for anyone about to change the native code. English only; see the [README](../README.md) for user-facing documentation.

## Sampling loop

A single WinForms timer ticks once per second on the UI thread and drives everything. One tick is:

1. `PdhGpuMemoryReader.Read()` — one PDH collection, five counter arrays. The engine array is optional: if it fails, the memory figures are still returned and only adapter load goes blank.
2. `GpuTelemetryEngine.GetTelemetry(adapterName)` — temperature, clock and load from the vendor driver.
3. Labels and overlay are updated from the values gathered above.

A second timer rebuilds the process list every ten seconds. That enumeration runs on a thread pool thread, because walking a few hundred processes is slow enough to show up as a stutter in the overlay.

## Where the numbers come from

| Value | Source |
|---|---|
| Per-process memory | PDH `\GPU Process Memory(*)\Dedicated Usage` and `\Shared Usage`, instance names carry the PID |
| Per-adapter memory | PDH `\GPU Adapter Memory(*)\Dedicated Usage` and `\Shared Usage`, instance names carry the LUID |
| Adapter load | PDH `\GPU Engine(*)\Utilization Percentage`, summed across processes per physical engine, then the busiest engine |
| Adapter names | DXGI `IDXGIFactory1::EnumAdapters1`, falling back to `D3DKMTQueryAdapterInfo` |
| Temperature, core clock | NVIDIA NVML and AMD ADL, matched to the adapter by PCI location |

Counters are added with `PdhAddEnglishCounterW`, so the counter paths above work on localised Windows installations as well.

Both memory pools are read because the dedicated one is frequently empty: integrated adapters have no dedicated memory, and on a hybrid laptop the discrete GPU keeps its allocations in the shared pool until something wakes it. A given adapter reports one instance per memory segment, so the values are summed per PID and per LUID.

## PDH buffer handling

`PdhGetFormattedCounterArrayW` writes an array of `PDH_FMT_COUNTERVALUE_ITEM_W` followed by the instance-name strings, all inside one caller-allocated block. The reader keeps that block alive between samples and only grows it, which keeps a per-second sample allocation-free apart from the parsed values.

Two consequences worth knowing:

- Instance names are read as `ReadOnlySpan<char>` straight out of the unmanaged buffer and parsed in place. Nothing may hold on to a name after the loop that produced it.
- `GpuReadResult` hands back the reader's own dictionaries rather than copies. They stay valid until the next `Read()`.

Every name pointer is bounds-checked against the buffer before it is dereferenced, and the struct layout is asserted in a static constructor — the code is x64-only and says so.

## DXGI without COM interop

`GpuAdapterCatalog` calls `EnumAdapters1` and `GetDesc1` through their vtable slots instead of declaring COM interfaces. It needs two methods on two interfaces, and this keeps the whole adapter catalogue to one file with no generated interop assembly. The slot numbers are derived from the `IUnknown` → `IDXGIObject` → `IDXGIFactory1` inheritance chain and are commented where they are declared; they are the one thing in the file that will break silently if edited carelessly, so `NativeLayoutTests` pins them and enumerates DXGI for real. `DXGI_ADAPTER_DESC1` must be marshalled with `CharSet.Unicode`; the default Ansi marshalling turns the description into garbage.

Software adapters (the Microsoft Basic Render Driver) are filtered out by `DXGI_ADAPTER_FLAG_SOFTWARE`.

## Vendor telemetry

Neither `nvml.dll` nor `atiadlxx.dll` ships with Windows, so both are loaded with `NativeLibrary.TryLoad` and every entry point is treated as optional. ADL writes the full `ADLODNPerformanceStatus` (eighteen ints) into the caller's buffer; the managed mirror must match it exactly, and ADL is not called at all if `Marshal.SizeOf` disagrees with the SDK size.

Both libraries load independently, so a machine with an NVIDIA and an AMD adapter gets readings for each. An adapter is matched to its vendor device by PCI bus, device and function: `D3DKMTQueryAdapterInfo(KMTQAITYPE_ADAPTERADDRESS)` gives the location for a LUID, NVML reports it through `nvmlDeviceGetPciInfo_v3`, and ADL through `ADL2_Adapter_AdapterInfo_Get`, which lists one entry per display output, so entries are collapsed per location. The location is resolved once per LUID and cached, because the lookup would otherwise run every second on the UI thread. Only when Windows reports no location does the name decide, and then only if it singles out exactly one device. There is no "first device" fallback: when nothing matches — an Intel iGPU, say — the sample comes back empty and the overlay drops the temperature and clock rows rather than showing another card's readings. `nvmlPciInfo_t` (68 bytes), `AdapterInfo` (1572 bytes) and `D3DKMT_ADAPTERADDRESS` (12 bytes) are size-checked like the ADL performance status, and the query that needs one is skipped if it disagrees.

## Process tracking

`ProcessTracker` decides which process is being monitored, without WinForms, so its rules are unit-tested. A PID is not an identity: Windows reuses PIDs and a restarted program returns under a new one, so the name and start time must agree every second. While the monitored process runs it is never swapped for a sibling with the same name, even when the search or the VRAM filter hides it; the picker keeps showing it. Once it exits, the next process-list refresh attaches to the remembered name, but only when exactly one running process carries it.

## Overlay

`OverlayForm` is a `WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` window drawn with `UpdateLayeredWindow`, which is what gives it per-pixel alpha over a fullscreen game without a compositor round trip. It never takes focus: `WM_MOUSEACTIVATE` is answered with `MA_NOACTIVATE`, and in locked mode `WM_NCHITTEST` returns `HTTRANSPARENT` so clicks land on whatever is underneath.

Rows are measured and laid out from the row list, so adding a metric is a matter of adding a row; the window resizes itself to fit. Rendering is skipped when neither the text nor the lock state changed, which is the common case between samples.

## Settings and diagnostics

`SettingsStore` writes through `settings.json.tmp` and a rename, so a crash mid-write leaves the previous file intact. It reads each property on its own: a field with the wrong type or an invalid hotkey falls back to its default without discarding the rest, and a file that is not JSON at all is copied to `settings.bad.json` before defaults are used. `MainForm` saves 1.5 seconds after the last change rather than only on exit. The selected GPU is stored as a LUID and a name, because a LUID is only unique until the next restart.

`Log` appends to `%LocalAppData%\Nook\nook.log`, rolling to `nook.1.log` at 1 MB, and never throws. Sampling failures are logged once per distinct message, not once per second. `Program` routes unhandled UI, AppDomain and task exceptions there, and the release build embeds its PDB so stack traces carry line numbers.

## Tests

`tests/Nook.Tests` covers the code whose failures are silent rather than loud: the counter instance-name parsing and engine-load aggregation, the byte and hotkey formatting, the GPU name shortening, saved-GPU resolution, vendor-device matching, process tracking, settings load and save, the log, and the hand-declared native layouts. A mistake in the LUID parser does not throw — it attributes memory to the wrong adapter — so the instance-name strings Windows actually produces are pinned as test data.

```powershell
dotnet test tests/Nook.Tests
```

One test does reach the hardware: DXGI enumeration runs for real, because the Microsoft Basic Render Driver is present even on a headless CI runner, and that is what catches a wrong vtable slot or marshalling. The rest is deliberately not covered. Vendor drivers and PDH counters need the hardware to answer, and the window and overlay need a desktop session; a mock of either would only prove that the mock behaves. The test project sees the internals through `InternalsVisibleTo`.
