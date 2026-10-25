using Nook;

namespace Nook.Tests;

/// <summary>
/// The saved GPU is a LUID, which Windows only keeps unique until the next restart. Resolving
/// it must survive a reboot without guessing between two cards of the same model.
/// </summary>
public class SelectionTests
{
    private static readonly GpuAdapterItem Intel = new(new GpuLuid(0xF7EB, 0), "Intel(R) UHD Graphics");
    private static readonly GpuAdapterItem Nvidia = new(new GpuLuid(0xFBDC, 0), "NVIDIA GeForce RTX 3050 Ti Laptop GPU");

    [Fact]
    public void FindAdapter_prefers_the_saved_luid()
    {
        Assert.Same(Nvidia, MainForm.FindAdapter([Intel, Nvidia], Nvidia.Luid, Intel.Name));
    }

    [Fact]
    public void FindAdapter_falls_back_to_the_name_when_the_luid_changed_after_a_reboot()
    {
        Assert.Same(Nvidia, MainForm.FindAdapter([Intel, Nvidia], new GpuLuid(0x1234, 0), Nvidia.Name));
    }

    [Fact]
    public void FindAdapter_does_not_guess_between_two_adapters_with_the_same_name()
    {
        var twin = Nvidia with { Luid = new GpuLuid(0xABCD, 0) };

        Assert.Same(Intel, MainForm.FindAdapter([Intel, Nvidia, twin], new GpuLuid(0x1234, 0), Nvidia.Name));
    }

    [Fact]
    public void FindAdapter_uses_the_first_adapter_when_nothing_was_saved()
    {
        Assert.Same(Intel, MainForm.FindAdapter([Intel, Nvidia], null, null));
    }

    [Fact]
    public void FindAdapter_returns_null_without_adapters()
    {
        Assert.Null(MainForm.FindAdapter([], Nvidia.Luid, Nvidia.Name));
    }

    [Theory]
    [InlineData(0x0002u | 0x0004u, 0x56u, "Ctrl+Shift+V")]
    [InlineData(0x0001u | 0x0008u, 0x31u, "Alt+Win+1")]
    [InlineData(0x0002u, 0x70u, "Ctrl+F1")]
    public void FormatHotkey_matches_the_windows_menu_spelling(uint modifiers, uint key, string expected)
    {
        Assert.Equal(expected, MainForm.FormatHotkey(modifiers, key));
    }
}
