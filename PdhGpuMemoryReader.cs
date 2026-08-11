using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Nook;

internal sealed class PdhGpuMemoryReader : IDisposable
{
    private const int PdhMoreData = unchecked((int)0x800007D2);
    private const int PdhNoData = unchecked((int)0x800007D5);
    private const int PdhCStatusNoInstance = unchecked((int)0x800007D1);
    private const int PdhCStatusInvalidData = unchecked((int)0xC0000BBA);
    private const int PdhInvalidData = unchecked((int)0xC0000BC6);
    private const uint PdhFmtLarge = 0x00000400;
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhCStatusValidData = 0;
    private const uint PdhCStatusNewData = 1;
    private const int CounterValueSizeX64 = 16;
    private const int CounterValueItemSizeX64 = 24;
    private const uint MaxCounterBufferBytes = 16 * 1024 * 1024;
    private const int MaxInstanceNameCharacters = 4096;

    private IntPtr _query;
    private IntPtr _processDedicatedCounter;
    private IntPtr _processSharedCounter;
    private IntPtr _adapterDedicatedCounter;
    private IntPtr _adapterSharedCounter;
    private IntPtr _engineUsageCounter;
    private readonly CounterBuffer _processDedicatedBuffer = new();
    private readonly CounterBuffer _processSharedBuffer = new();
    private readonly CounterBuffer _adapterDedicatedBuffer = new();
    private readonly CounterBuffer _adapterSharedBuffer = new();
    private readonly CounterBuffer _engineBuffer = new();

    private readonly Dictionary<int, long> _processDedicated = new();
    private readonly Dictionary<int, long> _processShared = new();
    private readonly Dictionary<GpuLuid, long> _adapterDedicated = new();
    private readonly Dictionary<GpuLuid, long> _adapterShared = new();
    private readonly Dictionary<GpuLuid, double> _adapterUsage = new();

    static PdhGpuMemoryReader()
    {
        if (IntPtr.Size != 8 || Marshal.SizeOf<PdhFmtCounterValue>() != CounterValueSizeX64 ||
            Marshal.SizeOf<PdhFmtCounterValueItem>() != CounterValueItemSizeX64)
        {
            throw new PlatformNotSupportedException("This reader requires the native x64 PDH counter-value layout.");
        }
    }

    public PdhGpuMemoryReader()
    {
        Check(PdhOpenQueryW(null, IntPtr.Zero, out _query), "PdhOpenQuery");
        try
        {
            // Integrated GPUs have no dedicated memory at all, and a discrete GPU parks most
            // of its allocations in the shared pool while it is idle, so both are collected.
            Check(PdhAddEnglishCounterW(_query, @"\GPU Process Memory(*)\Dedicated Usage", IntPtr.Zero, out _processDedicatedCounter), "PdhAddEnglishCounter");
            Check(PdhAddEnglishCounterW(_query, @"\GPU Process Memory(*)\Shared Usage", IntPtr.Zero, out _processSharedCounter), "PdhAddEnglishCounter");
            Check(PdhAddEnglishCounterW(_query, @"\GPU Adapter Memory(*)\Dedicated Usage", IntPtr.Zero, out _adapterDedicatedCounter), "PdhAddEnglishCounter");
            Check(PdhAddEnglishCounterW(_query, @"\GPU Adapter Memory(*)\Shared Usage", IntPtr.Zero, out _adapterSharedCounter), "PdhAddEnglishCounter");
            Check(PdhAddEnglishCounterW(_query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _engineUsageCounter), "PdhAddEnglishCounter");
            Check(PdhCollectQueryData(_query), "PdhCollectQueryData");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Takes one sample of all three counters. The dictionaries on the returned result are
    /// owned by the reader and are overwritten by the next call, so callers must consume
    /// them before sampling again.
    /// </summary>
    public GpuReadResult Read()
    {
        if (_query == IntPtr.Zero)
        {
            return GpuReadResult.Unavailable("Windows GPU counter reader is closed.");
        }

        var collectStatus = PdhCollectQueryData(_query);
        if (IsNoDataStatus(collectStatus))
        {
            return GpuReadResult.Unavailable("Windows has no GPU performance data available.");
        }

        if (collectStatus != 0)
        {
            return GpuReadResult.Unavailable($"Windows GPU counter read failed (0x{collectStatus:X8}).");
        }

        if (!TryCollect(_processDedicatedCounter, PdhFmtLarge, _processDedicatedBuffer, out var processDedicatedCount, out var error) ||
            !TryCollect(_processSharedCounter, PdhFmtLarge, _processSharedBuffer, out var processSharedCount, out error) ||
            !TryCollect(_adapterDedicatedCounter, PdhFmtLarge, _adapterDedicatedBuffer, out var adapterDedicatedCount, out error) ||
            !TryCollect(_adapterSharedCounter, PdhFmtLarge, _adapterSharedBuffer, out var adapterSharedCount, out error) ||
            !TryCollect(_engineUsageCounter, PdhFmtDouble, _engineBuffer, out var engineCount, out error))
        {
            return GpuReadResult.Unavailable(error!);
        }

        if (!TrySumByProcess(_processDedicatedBuffer, processDedicatedCount, _processDedicated, out error) ||
            !TrySumByProcess(_processSharedBuffer, processSharedCount, _processShared, out error) ||
            !TrySumByAdapter(_adapterDedicatedBuffer, adapterDedicatedCount, _adapterDedicated, out error) ||
            !TrySumByAdapter(_adapterSharedBuffer, adapterSharedCount, _adapterShared, out error))
        {
            return GpuReadResult.Unavailable(error!);
        }

        _adapterUsage.Clear();
        for (var index = 0; index < engineCount; index++)
        {
            if (!TryGetItem(_engineBuffer, index, out var name, out var value))
            {
                return GpuReadResult.Unavailable("Windows returned an invalid GPU counter instance name.");
            }

            if (!TryGetLuid(name, out var luid) || !IsValidCounterData(value.CStatus) ||
                double.IsNaN(value.DoubleValue) || double.IsInfinity(value.DoubleValue))
            {
                continue;
            }

            // Every engine (3D, copy, video decode, …) reports separately; the adapter is
            // as busy as its busiest engine, which is what Task Manager shows too.
            var usage = Math.Clamp(value.DoubleValue, 0, 100);
            if (!_adapterUsage.TryGetValue(luid, out var current) || usage > current)
            {
                _adapterUsage[luid] = usage;
            }
        }

        return GpuReadResult.Available(_processDedicated, _processShared, _adapterDedicated, _adapterShared, _adapterUsage);
    }

    /// <summary>
    /// Sums one memory counter per process. An adapter reports a separate instance for each
    /// of its memory segments, so a process can appear several times in the same array.
    /// </summary>
    private static bool TrySumByProcess(CounterBuffer buffer, uint itemCount, Dictionary<int, long> target, out string? error)
    {
        target.Clear();
        for (var index = 0; index < itemCount; index++)
        {
            if (!TryGetItem(buffer, index, out var name, out var value))
            {
                error = "Windows returned an invalid GPU counter instance name.";
                return false;
            }

            if (!TryGetProcessId(name, out var processId) || !IsValidCounterData(value.CStatus) || value.LargeValue < 0)
            {
                continue;
            }

            target.TryGetValue(processId, out var total);
            if (total <= long.MaxValue - value.LargeValue)
            {
                target[processId] = total + value.LargeValue;
            }
        }

        error = null;
        return true;
    }

    private static bool TrySumByAdapter(CounterBuffer buffer, uint itemCount, Dictionary<GpuLuid, long> target, out string? error)
    {
        target.Clear();
        for (var index = 0; index < itemCount; index++)
        {
            if (!TryGetItem(buffer, index, out var name, out var value))
            {
                error = "Windows returned an invalid GPU counter instance name.";
                return false;
            }

            if (!TryGetLuid(name, out var luid) || !IsValidCounterData(value.CStatus) || value.LargeValue < 0)
            {
                continue;
            }

            target.TryGetValue(luid, out var total);
            if (total <= long.MaxValue - value.LargeValue)
            {
                target[luid] = total + value.LargeValue;
            }
        }

        error = null;
        return true;
    }

    public void Dispose()
    {
        _processDedicatedBuffer.Dispose();
        _processSharedBuffer.Dispose();
        _adapterDedicatedBuffer.Dispose();
        _adapterSharedBuffer.Dispose();
        _engineBuffer.Dispose();
        if (_query != IntPtr.Zero)
        {
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }

    private static bool TryCollect(IntPtr counter, uint format, CounterBuffer counterBuffer, out uint itemCount, out string? error)
    {
        var bufferSize = counterBuffer.Size;
        var status = PdhGetFormattedCounterArrayW(counter, format, ref bufferSize, out itemCount, counterBuffer.Pointer);
        if (status == PdhMoreData)
        {
            if (counterBuffer.Size != 0)
            {
                bufferSize = 0;
                status = PdhGetFormattedCounterArrayW(counter, format, ref bufferSize, out itemCount, IntPtr.Zero);
            }

            if (status == PdhMoreData && bufferSize > 0 && bufferSize <= MaxCounterBufferBytes)
            {
                counterBuffer.Replace(bufferSize);
                bufferSize = counterBuffer.Size;
                status = PdhGetFormattedCounterArrayW(counter, format, ref bufferSize, out itemCount, counterBuffer.Pointer);
            }
        }

        if (IsNoDataStatus(status))
        {
            error = "Windows has no GPU counter sample available.";
            return false;
        }

        if (status == PdhMoreData && bufferSize > MaxCounterBufferBytes)
        {
            error = "Windows GPU counter buffer is too large.";
            return false;
        }

        if (status != 0 || itemCount > counterBuffer.Size / CounterValueItemSizeX64)
        {
            error = status == 0 ? "Windows returned an invalid GPU counter buffer." : $"Windows GPU counter read failed (0x{status:X8}).";
            return false;
        }

        counterBuffer.ItemCount = itemCount;
        error = null;
        return true;
    }

    /// <summary>
    /// Projects one counter entry out of the native buffer. The instance name stays in
    /// unmanaged memory: PDH returns hundreds of entries per second and none of the names
    /// outlive the parse below, so materialising strings for them is pure garbage.
    /// </summary>
    private static unsafe bool TryGetItem(CounterBuffer counterBuffer, int index, out ReadOnlySpan<char> name, out PdhFmtCounterValue value)
    {
        var item = (PdhFmtCounterValueItem*)(counterBuffer.Pointer + index * CounterValueItemSizeX64);
        value = item->Value;
        name = default;

        // PDH appends the instance names after the value array, inside the same buffer.
        var namesStart = counterBuffer.Pointer.ToInt64() + (long)counterBuffer.ItemCount * CounterValueItemSizeX64;
        var bufferEnd = counterBuffer.Pointer.ToInt64() + counterBuffer.Size;
        var address = item->Name.ToInt64();
        if (address < namesStart || address > bufferEnd - sizeof(char))
        {
            return false;
        }

        name = MemoryMarshal.CreateReadOnlySpanFromNullTerminated((char*)item->Name);
        return name.Length > 0 && name.Length <= MaxInstanceNameCharacters;
    }

    private static bool TryGetProcessId(ReadOnlySpan<char> name, out int processId)
    {
        processId = 0;
        if (!name.StartsWith("pid_", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var end = name[4..].IndexOf('_');
        return end > 0 && int.TryParse(name.Slice(4, end), NumberStyles.None, CultureInfo.InvariantCulture, out processId);
    }

    private static bool TryGetLuid(ReadOnlySpan<char> name, out GpuLuid luid)
    {
        luid = default;
        const string prefix = "luid_0x";
        var start = name.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return false;
        }

        start += prefix.Length;
        var separatorOffset = name[start..].IndexOf("_0x", StringComparison.OrdinalIgnoreCase);
        if (separatorOffset < 0)
        {
            return false;
        }

        var separator = start + separatorOffset;
        var lowStart = separator + 3;
        if (lowStart > name.Length)
        {
            return false;
        }

        var lowEndOffset = name[lowStart..].IndexOf('_');
        var lowEnd = lowEndOffset < 0 ? name.Length : lowStart + lowEndOffset;

        if (!uint.TryParse(name[start..separator], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var high) ||
            !uint.TryParse(name[lowStart..lowEnd], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var low))
        {
            return false;
        }

        luid = new GpuLuid(low, high);
        return true;
    }

    private static bool IsNoDataStatus(int status) => status == PdhNoData || status == PdhCStatusInvalidData || status == PdhInvalidData || status == PdhCStatusNoInstance;

    private static bool IsValidCounterData(uint status) => status == PdhCStatusValidData || status == PdhCStatusNewData;

    private static void Check(int status, string operation)
    {
        if (status != 0)
        {
            throw new Win32Exception(status, $"{operation} failed (0x{status:X8})");
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int PdhAddEnglishCounterW(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern int PdhCollectQueryData(IntPtr query);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern int PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr buffer);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern int PdhCloseQuery(IntPtr query);

    [StructLayout(LayoutKind.Explicit, Size = CounterValueItemSizeX64)]
    private struct PdhFmtCounterValueItem
    {
        [FieldOffset(0)] public IntPtr Name;
        [FieldOffset(8)] public PdhFmtCounterValue Value;
    }

    [StructLayout(LayoutKind.Explicit, Size = CounterValueSizeX64)]
    private struct PdhFmtCounterValue
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public long LargeValue;
        [FieldOffset(8)] public double DoubleValue;
    }

    private sealed class CounterBuffer : IDisposable
    {
        public IntPtr Pointer { get; private set; }
        public uint Size { get; private set; }

        /// <summary>Entries written by the most recent successful collection.</summary>
        public uint ItemCount { get; set; }

        public void Replace(uint size)
        {
            var replacement = Marshal.AllocHGlobal((int)size);
            var previous = Pointer;
            Pointer = replacement;
            Size = size;
            if (previous != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(previous);
            }
        }

        public void Dispose()
        {
            if (Pointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(Pointer);
                Pointer = IntPtr.Zero;
                Size = 0;
                ItemCount = 0;
            }
        }
    }
}

internal readonly record struct GpuLuid(uint LowPart, uint HighPart)
{
    public override string ToString() => $"LUID 0x{HighPart:X8}_0x{LowPart:X8}";
}

internal readonly record struct GpuReadResult(
    bool IsAvailable,
    IReadOnlyDictionary<int, long>? ProcessDedicatedBytes,
    IReadOnlyDictionary<int, long>? ProcessSharedBytes,
    IReadOnlyDictionary<GpuLuid, long>? AdapterDedicatedBytes,
    IReadOnlyDictionary<GpuLuid, long>? AdapterSharedBytes,
    IReadOnlyDictionary<GpuLuid, double>? AdapterUsagePercent,
    string? Error)
{
    public static GpuReadResult Available(
        IReadOnlyDictionary<int, long> processDedicated,
        IReadOnlyDictionary<int, long> processShared,
        IReadOnlyDictionary<GpuLuid, long> adapterDedicated,
        IReadOnlyDictionary<GpuLuid, long> adapterShared,
        IReadOnlyDictionary<GpuLuid, double> adapterUsage) =>
        new(true, processDedicated, processShared, adapterDedicated, adapterShared, adapterUsage, null);

    public static GpuReadResult Unavailable(string error) => new(false, null, null, null, null, null, error);
}
