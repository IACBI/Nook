using System.Runtime.InteropServices;

namespace Nook;

internal static class GpuAdapterCatalog
{
    private const int DxgiErrorNotFound = unchecked((int)0x887A0002);
    private const uint DxgiAdapterFlagSoftware = 2;
    private const uint KmtQueryAdapterRegistryInfo = 8;
    // IDXGIFactory1 and IDXGIAdapter1 inherit IDXGIObject (three IUnknown slots plus three object slots).
    private const int EnumAdapters1VtableSlot = 11;
    private const int GetDesc1VtableSlot = 9;
    private static readonly Guid Factory1Id = new("770AAE78-F26F-4DBA-A829-253C83D1B387");

    public static IReadOnlyList<GpuAdapterItem> GetAdapters()
    {
        var adapters = new List<GpuAdapterItem>();
        IntPtr factory = IntPtr.Zero;
        try
        {
            var factoryId = Factory1Id;
            var result = CreateDXGIFactory1(ref factoryId, out factory);
            if (result < 0)
            {
                return adapters;
            }

            // Windows exposes only a small number of display adapters. The bound
            // protects application startup from a faulty DXGI implementation that
            // keeps returning a non-terminal result for every index.
            for (uint index = 0; index < 32; index++)
            {
                var adapter = CallEnumAdapters1(factory, index, out var resultCode);
                if (adapter == IntPtr.Zero)
                {
                    break;
                }

                try
                {
                    if (resultCode >= 0 && CallGetDesc1(adapter, out var description) >= 0 && (description.Flags & DxgiAdapterFlagSoftware) == 0)
                    {
                        var name = GetDescription(description);
                        adapters.Add(new GpuAdapterItem(new GpuLuid(description.AdapterLuid.LowPart, unchecked((uint)description.AdapterLuid.HighPart)), name));
                    }
                }
                finally
                {
                    Marshal.Release(adapter);
                }

                if (resultCode == DxgiErrorNotFound || resultCode < 0)
                {
                    break;
                }
            }
        }
        catch (COMException)
        {
            // The PDH view remains usable even if DXGI enumeration is unavailable.
        }
        finally
        {
            if (factory != IntPtr.Zero)
            {
                Marshal.Release(factory);
            }
        }

        return adapters;
    }

    public static string GetAdapterName(GpuLuid luid)
    {
        try
        {
            var openAdapter = new D3dkmtOpenAdapterFromLuid
            {
                AdapterLuid = new NativeLuid { LowPart = luid.LowPart, HighPart = unchecked((int)luid.HighPart) }
            };
            if (D3DKMTOpenAdapterFromLuid(ref openAdapter) != 0 || openAdapter.AdapterHandle == 0)
            {
                return luid.ToString();
            }

            try
            {
                unsafe
                {
                    D3dkmtAdapterRegistryInfo registryInfo = default;
                    var query = new D3dkmtQueryAdapterInfo
                    {
                        AdapterHandle = openAdapter.AdapterHandle,
                        Type = KmtQueryAdapterRegistryInfo,
                        PrivateDriverData = (IntPtr)(&registryInfo),
                        PrivateDriverDataSize = (uint)sizeof(D3dkmtAdapterRegistryInfo)
                    };
                    if (D3DKMTQueryAdapterInfo(ref query) == 0)
                    {
                        var name = registryInfo.GetAdapterString();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            return name;
                        }
                    }
                }
            }
            finally
            {
                D3DKMTCloseAdapter(ref openAdapter.AdapterHandle);
            }
        }
        catch (EntryPointNotFoundException)
        {
            // Keep the LUID fallback on older Windows implementations.
        }

        return luid.ToString();
    }

    private static IntPtr CallEnumAdapters1(IntPtr factory, uint index, out int result)
    {
        var method = GetVtableMethod<EnumAdapters1Delegate>(factory, EnumAdapters1VtableSlot);
        result = method(factory, index, out var adapter);
        return adapter;
    }

    private static int CallGetDesc1(IntPtr adapter, out DxgiAdapterDesc1 description)
    {
        var method = GetVtableMethod<GetDesc1Delegate>(adapter, GetDesc1VtableSlot);
        return method(adapter, out description);
    }

    private static T GetVtableMethod<T>(IntPtr instance, int slot) where T : Delegate
    {
        var vtable = Marshal.ReadIntPtr(instance);
        var method = Marshal.ReadIntPtr(vtable, IntPtr.Size * slot);
        return Marshal.GetDelegateForFunctionPointer<T>(method);
    }

    private static unsafe string GetDescription(DxgiAdapterDesc1 description)
    {
        char* characters = description.Description;
        var name = new string(characters).Trim();
        return string.IsNullOrWhiteSpace(name) ? "Windows GPU" : name;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(IntPtr factory, uint adapterIndex, out IntPtr adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Delegate(IntPtr adapter, out DxgiAdapterDesc1 description);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int D3DKMTOpenAdapterFromLuid(ref D3dkmtOpenAdapterFromLuid openAdapter);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int D3DKMTQueryAdapterInfo(ref D3dkmtQueryAdapterInfo queryAdapterInfo);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int D3DKMTCloseAdapter(ref uint adapterHandle);

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct DxgiAdapterDesc1
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public NativeLuid AdapterLuid;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeLuid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3dkmtOpenAdapterFromLuid
    {
        public NativeLuid AdapterLuid;
        public uint AdapterHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3dkmtQueryAdapterInfo
    {
        public uint AdapterHandle;
        public uint Type;
        public IntPtr PrivateDriverData;
        public uint PrivateDriverDataSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct D3dkmtAdapterRegistryInfo
    {
        private fixed char _adapterString[260];
        private fixed char _biosString[260];
        private fixed char _dacType[260];
        private fixed char _chipType[260];

        public string GetAdapterString()
        {
            fixed (char* characters = _adapterString)
            {
                return new string(characters).Trim();
            }
        }
    }
}

internal sealed record GpuAdapterItem(GpuLuid Luid, string Name)
{
    public override string ToString() => Name;
}
