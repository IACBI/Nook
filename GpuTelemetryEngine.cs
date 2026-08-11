using System.Runtime.InteropServices;
using System.Text;

namespace Nook;

/// <summary>
/// Reads temperature, core clock and utilisation from the vendor driver libraries.
/// NVML is preferred when present; AMD's ADL is the fallback. Neither ships with
/// Windows, so every entry point is resolved late and every failure is non-fatal.
/// </summary>
internal sealed class GpuTelemetryEngine : IDisposable
{
    private static readonly NvmlNative.ADL_Main_Memory_Alloc AdlAllocDelegate = Marshal.AllocHGlobal;

    private readonly List<NvmlDevice> _nvmlDevices = [];
    private readonly bool _nvmlAvailable;
    private readonly bool _adlAvailable;
    private IntPtr _adlContext = IntPtr.Zero;
    private IntPtr _nvmlLibHandle;
    private IntPtr _adlLibHandle;

    public GpuTelemetryEngine()
    {
        try
        {
            if (NativeLibrary.TryLoad("nvml.dll", typeof(GpuTelemetryEngine).Assembly, DllImportSearchPath.System32 | DllImportSearchPath.SafeDirectories, out _nvmlLibHandle)
                && NvmlNative.nvmlInit_v2() == NvmlNative.NvmlReturn.Success)
            {
                LoadNvmlDevices();
                _nvmlAvailable = _nvmlDevices.Count > 0;
            }
        }
        catch
        {
            _nvmlAvailable = false;
        }

        try
        {
            if (!_nvmlAvailable
                && NativeLibrary.TryLoad("atiadlxx.dll", typeof(GpuTelemetryEngine).Assembly, DllImportSearchPath.SafeDirectories, out _adlLibHandle)
                && NvmlNative.ADL2_Main_Control_Create(AdlAllocDelegate, 1, out _adlContext) == 0)
            {
                _adlAvailable = true;
            }
        }
        catch
        {
            _adlAvailable = false;
        }
    }

    /// <summary>
    /// Returns the readings for the adapter Windows reports under <paramref name="adapterName"/>,
    /// or an empty sample when no vendor library covers that adapter.
    /// </summary>
    public GpuTelemetrySample GetTelemetry(string? adapterName)
    {
        if (_nvmlAvailable && IsVendor(adapterName, "NVIDIA"))
        {
            return ReadNvml(MatchNvmlDevice(adapterName));
        }

        if (_adlAvailable && _adlContext != IntPtr.Zero && (IsVendor(adapterName, "AMD") || IsVendor(adapterName, "Radeon")))
        {
            return ReadAdl();
        }

        return default;
    }

    public void Dispose()
    {
        if (_nvmlAvailable)
        {
            try { NvmlNative.nvmlShutdown(); } catch { /* the driver may already be gone */ }
        }

        if (_adlAvailable && _adlContext != IntPtr.Zero)
        {
            try { NvmlNative.ADL2_Main_Control_Destroy(_adlContext); } catch { /* the driver may already be gone */ }
            _adlContext = IntPtr.Zero;
        }

        _nvmlDevices.Clear();
        FreeLibrary(ref _nvmlLibHandle);
        FreeLibrary(ref _adlLibHandle);
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
                _nvmlDevices.Add(new NvmlDevice(handle, ReadNvmlName(handle)));
            }
        }
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

    private IntPtr MatchNvmlDevice(string? adapterName)
    {
        if (!string.IsNullOrWhiteSpace(adapterName))
        {
            foreach (var device in _nvmlDevices)
            {
                if (string.Equals(device.Name, adapterName, StringComparison.OrdinalIgnoreCase))
                {
                    return device.Handle;
                }
            }
        }

        // The DXGI description and the NVML product name occasionally differ in
        // wording; on a single-NVIDIA machine the first device is still the right one.
        return _nvmlDevices[0].Handle;
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

    private GpuTelemetrySample ReadAdl()
    {
        double? temp = null;
        uint? clock = null;
        double? usage = null;

        try
        {
            if (NvmlNative.ADL2_OverdriveN_Temperature_Get(_adlContext, 0, 1, out var milliCelsius) == 0)
            {
                temp = milliCelsius / 1000.0;
            }

            if (NvmlNative.ADL2_OverdriveN_PerformanceStatus_Get(_adlContext, 0, out var status) == 0 && status.iCoreClock >= 0)
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

    private static bool IsVendor(string? adapterName, string vendor) =>
        string.IsNullOrWhiteSpace(adapterName) || adapterName.Contains(vendor, StringComparison.OrdinalIgnoreCase);

    private static void FreeLibrary(ref IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        try { NativeLibrary.Free(handle); } catch { /* nothing left to release */ }
        handle = IntPtr.Zero;
    }

    private readonly record struct NvmlDevice(IntPtr Handle, string Name);

    private static class NvmlNative
    {
        public const int NvmlDeviceNameBufferSize = 96;

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

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate IntPtr ADL_Main_Memory_Alloc(int size);

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
        }

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32 | DllImportSearchPath.SafeDirectories)]
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern NvmlReturn nvmlInit_v2();

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32 | DllImportSearchPath.SafeDirectories)]
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern NvmlReturn nvmlShutdown();

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32 | DllImportSearchPath.SafeDirectories)]
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern NvmlReturn nvmlDeviceGetCount_v2(out uint count);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32 | DllImportSearchPath.SafeDirectories)]
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern NvmlReturn nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32 | DllImportSearchPath.SafeDirectories)]
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern NvmlReturn nvmlDeviceGetName(IntPtr device, byte[] name, uint length);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32 | DllImportSearchPath.SafeDirectories)]
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern NvmlReturn nvmlDeviceGetTemperature(IntPtr device, NvmlTemperatureSensors sensorType, out uint temp);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32 | DllImportSearchPath.SafeDirectories)]
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern NvmlReturn nvmlDeviceGetClockInfo(IntPtr device, NvmlClockType type, out uint clock);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32 | DllImportSearchPath.SafeDirectories)]
        [DllImport("nvml.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern NvmlReturn nvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int ADL2_Main_Control_Create(ADL_Main_Memory_Alloc callback, int enumConnectedAdapters, out IntPtr context);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int ADL2_Main_Control_Destroy(IntPtr context);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int ADL2_OverdriveN_Temperature_Get(IntPtr context, int adapterIndex, int temperatureType, out int temperature);

        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int ADL2_OverdriveN_PerformanceStatus_Get(IntPtr context, int adapterIndex, out ADLODNPerformanceStatus odPerformanceStatus);
    }
}

internal readonly record struct GpuTelemetrySample(double? TempCelsius, uint? CoreClockMhz, double? UsagePercent);
