using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Nook;

/// <summary>
/// Reads temperature, core clock and utilisation from the vendor driver libraries.
/// NVML and ADL are both loaded when present, and each adapter is matched to its vendor
/// device by PCI location. Neither library ships with Windows, so every entry point is
/// resolved late and every failure is non-fatal.
/// </summary>
internal sealed class GpuTelemetryEngine : IDisposable
{
    /// <summary>Size of <c>ADLODNPerformanceStatus</c> in the ADL SDK: eighteen 32-bit ints.</summary>
    internal const int AdlPerformanceStatusNativeSize = 18 * sizeof(int);

    /// <summary>Size of <c>nvmlPciInfo_t</c> in the NVML header: 68 bytes.</summary>
    internal const int NvmlPciInfoNativeSize = 68;

    /// <summary>Size of <c>AdapterInfo</c> in the Windows ADL SDK: 1572 bytes.</summary>
    internal const int AdapterInfoNativeSize = 1572;

    private static readonly NvmlNative.ADL_Main_Memory_Alloc AdlAllocDelegate = Marshal.AllocHGlobal;

    /// <summary>
    /// Size of the managed mirror of <c>ADLODNPerformanceStatus</c>. ADL is never called unless it
    /// equals <see cref="AdlPerformanceStatusNativeSize"/>; temperature and clock are lost instead.
    /// </summary>
    internal static int AdlPerformanceStatusManagedSize => Marshal.SizeOf<NvmlNative.ADLODNPerformanceStatus>();

    /// <summary>Size of the managed mirror of <c>nvmlPciInfo_t</c>. The PCI query is skipped unless it equals <see cref="NvmlPciInfoNativeSize"/>.</summary>
    internal static int NvmlPciInfoManagedSize => Marshal.SizeOf<NvmlNative.NvmlPciInfo>();

    /// <summary>Size of the managed mirror of <c>AdapterInfo</c>. Enumeration is skipped unless it equals <see cref="AdapterInfoNativeSize"/>.</summary>
    internal static int AdapterInfoManagedSize => Marshal.SizeOf<NvmlNative.AdapterInfo>();

    private readonly List<(GpuPciAddress? Address, string Name)> _nvmlDevices = [];
    private readonly List<IntPtr> _nvmlHandles = [];
    private readonly List<AdlAdapter> _adlAdapters = [];
    private readonly Dictionary<GpuLuid, GpuPciAddress?> _pciAddresses = [];
    private bool _nvmlInitialized;
    private bool _adlAvailable;
    private IntPtr _adlContext = IntPtr.Zero;
    private IntPtr _nvmlLibHandle;
    private IntPtr _adlLibHandle;

    public GpuTelemetryEngine()
    {
        try
        {
            // System32 only: the application directory must never be a source of native code (DLL preloading). Both vendors' drivers install these libraries there.
            if (NativeLibrary.TryLoad("nvml.dll", typeof(GpuTelemetryEngine).Assembly, DllImportSearchPath.System32, out _nvmlLibHandle)
                && NvmlNative.nvmlInit_v2() == NvmlNative.NvmlReturn.Success)
            {
                // Set before enumerating, so Dispose shuts NVML down even when no device is found.
                _nvmlInitialized = true;
                LoadNvmlDevices();
            }
        }
        catch
        {
            // NVML is optional; a failure leaves the NVIDIA path without readings.
        }

        try
        {
            if (AdlPerformanceStatusManagedSize == AdlPerformanceStatusNativeSize
                && NativeLibrary.TryLoad("atiadlxx.dll", typeof(GpuTelemetryEngine).Assembly, DllImportSearchPath.System32, out _adlLibHandle)
                && NvmlNative.ADL2_Main_Control_Create(AdlAllocDelegate, 1, out _adlContext) == 0)
            {
                _adlAvailable = true;
                LoadAdlAdapters();
            }
        }
        catch
        {
            _adlAvailable = false;
        }
    }

    /// <summary>
    /// Returns the readings for the adapter identified by <paramref name="luid"/>, or an empty sample
    /// when no vendor library covers that adapter. A device is matched by PCI location, so another
    /// card's readings are never shown. The adapter name is consulted only when Windows does not
    /// report a PCI location.
    /// </summary>
    public GpuTelemetrySample GetTelemetry(GpuLuid luid, string? adapterName)
    {
        var address = ResolvePciAddress(luid);

        var nvmlIndex = ResolveDeviceIndex(_nvmlDevices, address, adapterName);
        if (nvmlIndex >= 0)
        {
            return ReadNvml(_nvmlHandles[nvmlIndex]);
        }

        var adlIndex = ResolveAdlAdapterIndex(_adlAdapters, address, adapterName);
        if (adlIndex >= 0 && _adlAvailable && _adlContext != IntPtr.Zero)
        {
            return ReadAdl(adlIndex);
        }

        return default;
    }

    /// <summary>
    /// Picks the NVML device for an adapter. A known PCI location is authoritative, so a device at
    /// another address is never a match even when the names agree. The name is used only when the
    /// location is unknown, and only when it singles out exactly one device.
    /// </summary>
    internal static int ResolveDeviceIndex(IReadOnlyList<(GpuPciAddress? Address, string Name)> devices, GpuPciAddress? target, string? adapterName)
    {
        if (target is { } address)
        {
            for (var index = 0; index < devices.Count; index++)
            {
                if (devices[index].Address == address)
                {
                    return index;
                }
            }

            return -1;
        }

        if (string.IsNullOrWhiteSpace(adapterName))
        {
            return -1;
        }

        var match = -1;
        for (var index = 0; index < devices.Count; index++)
        {
            if (string.Equals(devices[index].Name, adapterName, StringComparison.OrdinalIgnoreCase))
            {
                if (match >= 0)
                {
                    return -1;
                }

                match = index;
            }
        }

        return match;
    }

    /// <summary>
    /// Picks the ADL adapter index for an adapter, by PCI location when known. Without a location, the
    /// adapter is taken only when ADL reports exactly one physical AMD adapter and the Windows name
    /// says AMD or Radeon.
    /// </summary>
    internal static int ResolveAdlAdapterIndex(IReadOnlyList<AdlAdapter> adapters, GpuPciAddress? target, string? adapterName)
    {
        if (target is { } address)
        {
            foreach (var adapter in adapters)
            {
                if (adapter.Address == address)
                {
                    return adapter.Index;
                }
            }

            return -1;
        }

        if (adapters.Count != 1 || string.IsNullOrWhiteSpace(adapterName))
        {
            return -1;
        }

        var isAmd = adapterName.Contains("AMD", StringComparison.OrdinalIgnoreCase) || adapterName.Contains("Radeon", StringComparison.OrdinalIgnoreCase);
        return isAmd ? adapters[0].Index : -1;
    }

    /// <summary>Reads the function number from an NVML bus id such as <c>00000000:01:00.1</c>. Returns 0 when it cannot be read.</summary>
    internal static uint ParsePciFunction(string busId)
    {
        var dot = busId.LastIndexOf('.');
        if (dot < 0 || !uint.TryParse(busId.AsSpan(dot + 1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var function) || function > 7)
        {
            return 0;
        }

        return function;
    }

    public void Dispose()
    {
        if (_nvmlInitialized)
        {
            try { NvmlNative.nvmlShutdown(); } catch { /* the driver may already be gone */ }
        }

        if (_adlAvailable && _adlContext != IntPtr.Zero)
        {
            try { NvmlNative.ADL2_Main_Control_Destroy(_adlContext); } catch { /* the driver may already be gone */ }
            _adlContext = IntPtr.Zero;
        }

        _nvmlDevices.Clear();
        _nvmlHandles.Clear();
        _adlAdapters.Clear();
        FreeLibrary(ref _nvmlLibHandle);
        FreeLibrary(ref _adlLibHandle);
    }

    private GpuPciAddress? ResolvePciAddress(GpuLuid luid)
    {
        // The PCI location never changes while Windows runs, and this is called every second on the UI thread.
        if (!_pciAddresses.TryGetValue(luid, out var address))
        {
            address = GpuAdapterCatalog.TryGetPciAddress(luid, out var resolved) ? (GpuPciAddress?)resolved : null;
            _pciAddresses[luid] = address;
        }

        return address;
    }

    private void LoadNvmlDevices()
    {
        if (NvmlNative.nvmlDeviceGetCount_v2(out var count) != NvmlNative.NvmlReturn.Success)
        {
            return;
        }

        for (uint index = 0; index < count; index++)
        {
            if (NvmlNative.nvmlDeviceGetHandleByIndex_v2(index, out var handle) == NvmlNative.NvmlReturn.Success)
            {
                _nvmlDevices.Add((ReadNvmlPciAddress(handle), ReadNvmlName(handle)));
                _nvmlHandles.Add(handle);
            }
        }
    }

    private void LoadAdlAdapters()
    {
        if (AdapterInfoManagedSize != AdapterInfoNativeSize)
        {
            return;
        }

        if (NvmlNative.ADL2_Adapter_NumberOfAdapters_Get(_adlContext, out var count) != 0 || count <= 0)
        {
            return;
        }

        var size = count * AdapterInfoNativeSize;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            unsafe
            {
                new Span<byte>((void*)buffer, size).Clear();
            }

            // ADL checks each entry's iSize to learn how much room it has; leaving it zero fails the call.
            for (var index = 0; index < count; index++)
            {
                Marshal.WriteInt32(buffer, index * AdapterInfoNativeSize, AdapterInfoNativeSize);
            }

            if (NvmlNative.ADL2_Adapter_AdapterInfo_Get(_adlContext, buffer, size) != 0)
            {
                return;
            }

            // ADL reports one entry per display output, so several entries can share a physical adapter.
            var physical = new List<(AdlAdapter Adapter, bool Present)>();
            var positions = new Dictionary<GpuPciAddress, int>();
            for (var index = 0; index < count; index++)
            {
                var adapter = ReadAdlAdapter(IntPtr.Add(buffer, index * AdapterInfoNativeSize), out var present);
                if (!positions.TryGetValue(adapter.Address, out var position))
                {
                    positions[adapter.Address] = physical.Count;
                    physical.Add((adapter, present));
                }
                else if (!physical[position].Present && present)
                {
                    physical[position] = (adapter, present);
                }
            }

            _adlAdapters.AddRange(physical.Select(entry => entry.Adapter));
        }
        catch
        {
            // Without the adapter list ADL cannot be matched to any card, so ADL readings stay empty.
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static unsafe AdlAdapter ReadAdlAdapter(IntPtr entry, out bool present)
    {
        var info = (NvmlNative.AdapterInfo*)entry;
        present = info->iPresent != 0;
        return new AdlAdapter(info->iAdapterIndex, new GpuPciAddress((uint)info->iBusNumber, (uint)info->iDeviceNumber, (uint)info->iFunctionNumber));
    }

    private static string ReadNvmlName(IntPtr device)
    {
        var buffer = new byte[NvmlNative.NvmlDeviceNameBufferSize];
        if (NvmlNative.nvmlDeviceGetName(device, buffer, (uint)buffer.Length) != NvmlNative.NvmlReturn.Success)
        {
            return string.Empty;
        }

        var length = Array.IndexOf(buffer, (byte)0);
        return Encoding.UTF8.GetString(buffer, 0, length < 0 ? buffer.Length : length);
    }

    private static GpuPciAddress? ReadNvmlPciAddress(IntPtr device)
    {
        if (NvmlPciInfoManagedSize != NvmlPciInfoNativeSize)
        {
            return null;
        }

        if (NvmlNative.nvmlDeviceGetPciInfo_v3(device, out var pci) != NvmlNative.NvmlReturn.Success)
        {
            return null;
        }

        // NVML gives the bus and device as numbers but the function only inside the busId string.
        return new GpuPciAddress(pci.bus, pci.device, ParsePciFunction(ReadBusId(pci)));
    }

    private static unsafe string ReadBusId(NvmlNative.NvmlPciInfo pci)
    {
        var text = Encoding.ASCII.GetString(pci.busId, NvmlNative.NvmlBusIdBufferSize);
        var end = text.IndexOf('\0');
        return end < 0 ? text : text[..end];
    }

    private static GpuTelemetrySample ReadNvml(IntPtr device)
    {
        double? temp = null;
        uint? clock = null;
        double? usage = null;

        try
        {
            if (NvmlNative.nvmlDeviceGetTemperature(device, NvmlNative.NvmlTemperatureSensors.Gpu, out var celsius) == NvmlNative.NvmlReturn.Success)
            {
                temp = celsius;
            }

            if (NvmlNative.nvmlDeviceGetClockInfo(device, NvmlNative.NvmlClockType.Graphics, out var megahertz) == NvmlNative.NvmlReturn.Success)
            {
                clock = megahertz;
            }

            if (NvmlNative.nvmlDeviceGetUtilizationRates(device, out var utilization) == NvmlNative.NvmlReturn.Success)
            {
                usage = utilization.gpu;
            }
        }
        catch
        {
            // A driver reset invalidates the device handle until the next launch.
        }

        return new GpuTelemetrySample(temp, clock, usage);
    }

    private GpuTelemetrySample ReadAdl(int adapterIndex)
    {
        double? temp = null;
        uint? clock = null;
        double? usage = null;

        try
        {
            if (NvmlNative.ADL2_OverdriveN_Temperature_Get(_adlContext, adapterIndex, 1, out var milliCelsius) == 0)
            {
                temp = milliCelsius / 1000.0;
            }

            if (NvmlNative.ADL2_OverdriveN_PerformanceStatus_Get(_adlContext, adapterIndex, out var status) == 0 && status.iCoreClock >= 0)
            {
                clock = (uint)status.iCoreClock;
                usage = status.iGPUActivityPercent;
            }
        }
        catch
        {
            // A driver reset invalidates the ADL context until the next launch.
        }

        return new GpuTelemetrySample(temp, clock, usage);
    }

    private static void FreeLibrary(ref IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        try { NativeLibrary.Free(handle); } catch { /* nothing left to release */ }
        handle = IntPtr.Zero;
    }

    private static class NvmlNative
    {
        public const int NvmlDeviceNameBufferSize = 96;
        public const int NvmlBusIdBufferSize = 32;

        public enum NvmlReturn : int
        {
            Success = 0
        }

        public enum NvmlTemperatureSensors : int
        {
            Gpu = 0
        }

        public enum NvmlClockType : int
        {
            Graphics = 0
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NvmlUtilization
        {
            public uint gpu;
            public uint memory;
        }

        // Mirrors nvmlPciInfo_t. The fixed buffers are public only so the compiler does not
        // report them as unused; nothing writes them.
        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct NvmlPciInfo
        {
            public fixed byte busIdLegacy[16];
            public uint domain;
            public uint bus;
            public uint device;
            public uint pciDeviceId;
            public uint pciSubSystemId;
            public fixed byte busId[NvmlBusIdBufferSize];
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate IntPtr ADL_Main_Memory_Alloc(int size);

        // Mirrors adl_structures.h field for field. The driver writes the whole native size
        // into the caller's buffer, so a shorter declaration is a stack overrun, not a truncation.
        [StructLayout(LayoutKind.Sequential)]
        public struct ADLODNPerformanceStatus
        {
            public int iCoreClock;
            public int iMemoryClock;
            public int iDCEFClock;
            public int iGFXClock;
            public int iUVDClock;
            public int iVCEClock;
            public int iGPUActivityPercent;
            public int iCurrentCorePerformanceLevel;
            public int iCurrentMemoryPerformanceLevel;
            public int iCurrentDCEFPerformanceLevel;
            public int iCurrentGFXPerformanceLevel;
            public int iUVDPerformanceLevel;
            public int iVCEPerformanceLevel;
            public int iCurrentBusSpeed;
            public int iCurrentBusLanes;
            public int iMaximumBusLanes;
            public int iVDDC;
            public int iVDDCI;
        }

        // Mirrors the Windows AdapterInfo from the ADL SDK. The string fields are public fixed
        // buffers only so the compiler does not report them as unused; they are never read here.
        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct AdapterInfo
        {
            public int iSize;
            public int iAdapterIndex;
            public fixed byte strUDID[256];
            public int iBusNumber;
            public int iDeviceNumber;
            public int iFunctionNumber;
            public int iVendorID;
            public fixed byte strAdapterName[256];
            public fixed byte strDisplayName[256];
            public int iPresent;
            public int iExist;
            public fixed byte strDriverPath[256];
            public fixed byte strDriverPathExt[256];
            public fixed byte strPNPString[256];
            public int iOSDisplayIndex;
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern NvmlReturn nvmlInit_v2();

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern NvmlReturn nvmlShutdown();

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern NvmlReturn nvmlDeviceGetCount_v2(out uint count);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern NvmlReturn nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern NvmlReturn nvmlDeviceGetName(IntPtr device, byte[] name, uint length);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern NvmlReturn nvmlDeviceGetPciInfo_v3(IntPtr device, out NvmlPciInfo pci);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern NvmlReturn nvmlDeviceGetTemperature(IntPtr device, NvmlTemperatureSensors sensorType, out uint temp);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern NvmlReturn nvmlDeviceGetClockInfo(IntPtr device, NvmlClockType type, out uint clock);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern NvmlReturn nvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int ADL2_Main_Control_Create(ADL_Main_Memory_Alloc callback, int enumConnectedAdapters, out IntPtr context);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int ADL2_Main_Control_Destroy(IntPtr context);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int ADL2_Adapter_NumberOfAdapters_Get(IntPtr context, out int count);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int ADL2_Adapter_AdapterInfo_Get(IntPtr context, IntPtr info, int inputSizeInBytes);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int ADL2_OverdriveN_Temperature_Get(IntPtr context, int adapterIndex, int temperatureType, out int temperature);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int ADL2_OverdriveN_PerformanceStatus_Get(IntPtr context, int adapterIndex, out ADLODNPerformanceStatus odPerformanceStatus);
    }
}

internal readonly record struct GpuTelemetrySample(double? TempCelsius, uint? CoreClockMhz, double? UsagePercent);

/// <summary>One physical AMD adapter as ADL numbers it, with the PCI location that identifies it.</summary>
internal readonly record struct AdlAdapter(int Index, GpuPciAddress Address);
