using Nook;

namespace Nook.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Nook.Tests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "settings.json");
    private string BadPath => Path.Combine(_directory, "settings.bad.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Every_property_survives_a_save_and_load()
    {
        var settings = new AppSettings
        {
            SelectedProcessName = "game.exe",
            OverlayVisible = false,
            OverlayLocked = false,
            OverlayX = 120,
            OverlayY = -40,
            MinimizeToTray = false,
            CloseToTray = false,
            StartWithWindows = true,
            HotkeyToggleOverlayKey = 0x41,
            HotkeyToggleOverlayModifiers = 0x0001 | 0x4000,
            HotkeyToggleLockKey = 0x42,
            HotkeyToggleLockModifiers = 0x0008 | 0x0004,
            OnlyShowVramProcesses = false,
        };
        settings.SetSelectedGpu(new GpuLuid(0x0000F633, 0x00000001), "NVIDIA GeForce RTX 3050 Ti Laptop GPU");

        Assert.True(SettingsStore.Save(settings, SettingsPath));
        var loaded = SettingsStore.Load(SettingsPath);

        Assert.Equal("game.exe", loaded.SelectedProcessName);
        Assert.Equal(new GpuLuid(0x0000F633, 0x00000001), loaded.SelectedGpu);
        Assert.Equal("NVIDIA GeForce RTX 3050 Ti Laptop GPU", loaded.SelectedGpuName);
        Assert.False(loaded.OverlayVisible);
        Assert.False(loaded.OverlayLocked);
        Assert.Equal(120, loaded.OverlayX);
        Assert.Equal(-40, loaded.OverlayY);
        Assert.False(loaded.MinimizeToTray);
        Assert.False(loaded.CloseToTray);
        Assert.True(loaded.StartWithWindows);
        Assert.Equal(0x41u, loaded.HotkeyToggleOverlayKey);
        Assert.Equal(0x4001u, loaded.HotkeyToggleOverlayModifiers);
        Assert.Equal(0x42u, loaded.HotkeyToggleLockKey);
        Assert.Equal(0x000Cu, loaded.HotkeyToggleLockModifiers);
        Assert.False(loaded.OnlyShowVramProcesses);
    }

    [Fact]
    public void A_missing_file_yields_defaults_without_a_backup()
    {
        AssertIsDefault(SettingsStore.Load(SettingsPath));
        Assert.False(File.Exists(BadPath));
    }

    [Fact]
    public void Invalid_json_yields_defaults_and_keeps_the_original_bytes()
    {
        WriteFile("{ \"OverlayX\": 120,");
        var original = File.ReadAllBytes(SettingsPath);

        AssertIsDefault(SettingsStore.Load(SettingsPath));
        Assert.Equal(original, File.ReadAllBytes(BadPath));
    }

    [Fact]
    public void A_wrong_typed_field_and_an_invalid_hotkey_default_while_the_rest_loads()
    {
        WriteFile("""
            {
              "SelectedProcessName": "game.exe",
              "OverlayX": 120,
              "OverlayY": -40,
              "OnlyShowVramProcesses": "false",
              "HotkeyToggleOverlayKey": 0,
              "HotkeyToggleOverlayModifiers": 3,
              "HotkeyToggleLockKey": -1,
              "HotkeyToggleLockModifiers": 6
            }
            """);

        var loaded = SettingsStore.Load(SettingsPath);

        Assert.Equal("game.exe", loaded.SelectedProcessName);
        Assert.Equal(120, loaded.OverlayX);
        Assert.Equal(-40, loaded.OverlayY);
        Assert.True(loaded.OnlyShowVramProcesses);
        Assert.Equal(0x56u, loaded.HotkeyToggleOverlayKey);
        Assert.Equal(0x6u, loaded.HotkeyToggleOverlayModifiers);
        Assert.Equal(0x4Cu, loaded.HotkeyToggleLockKey);
        Assert.Equal(0x6u, loaded.HotkeyToggleLockModifiers);
        Assert.False(File.Exists(BadPath));
    }

    [Theory]
    [InlineData(0, 6)]   // virtual key below 0x01
    [InlineData(255, 6)] // virtual key above 0xFE
    [InlineData(86, 0)]  // no modifier at all
    [InlineData(86, 16)] // only an unknown flag bit
    [InlineData(86, 18)] // Ctrl plus an unknown flag bit
    public void An_invalid_hotkey_falls_back_as_a_pair(uint key, uint modifiers)
    {
        WriteFile($$"""{ "HotkeyToggleOverlayKey": {{key}}, "HotkeyToggleOverlayModifiers": {{modifiers}} }""");

        var loaded = SettingsStore.Load(SettingsPath);

        Assert.Equal(0x56u, loaded.HotkeyToggleOverlayKey);
        Assert.Equal(0x6u, loaded.HotkeyToggleOverlayModifiers);
    }

    [Fact]
    public void An_oversized_file_yields_defaults_and_is_kept_aside()
    {
        WriteFile("{ \"OverlayX\": 120, \"Padding\": \"" + new string('x', 64 * 1024) + "\" }");
        var original = File.ReadAllBytes(SettingsPath);

        var loaded = SettingsStore.Load(SettingsPath);

        Assert.Equal(new AppSettings().OverlayX, loaded.OverlayX);
        Assert.Equal(original, File.ReadAllBytes(BadPath));
    }

    [Fact]
    public void Save_leaves_no_temporary_file_behind()
    {
        Assert.True(SettingsStore.Save(new AppSettings(), SettingsPath));

        Assert.True(File.Exists(SettingsPath));
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void Save_replaces_an_existing_profile()
    {
        Assert.True(SettingsStore.Save(new AppSettings { OverlayX = 1 }, SettingsPath));
        Assert.True(SettingsStore.Save(new AppSettings { OverlayX = 2 }, SettingsPath));

        Assert.Equal(2, SettingsStore.Load(SettingsPath).OverlayX);
    }

    [Fact]
    public void Save_reports_failure_instead_of_throwing()
    {
        WriteFile("{}");

        // The target's parent is a regular file, so the directory cannot be created.
        Assert.False(SettingsStore.Save(new AppSettings(), Path.Combine(SettingsPath, "nested", "settings.json")));
    }

    [Fact]
    public void A_profile_written_by_the_previous_serializer_still_loads()
    {
        WriteFile("""
            {
              "SelectedProcessName": "game.exe",
              "SelectedGpuLowPart": 63027,
              "SelectedGpuHighPart": 0,
              "SelectedGpu": {
                "LowPart": 63027,
                "HighPart": 0
              },
              "OverlayVisible": false,
              "OverlayLocked": false,
              "OverlayX": 120,
              "OverlayY": -40,
              "MinimizeToTray": false,
              "CloseToTray": false,
              "StartWithWindows": true,
              "HotkeyToggleOverlayKey": 86,
              "HotkeyToggleOverlayModifiers": 6,
              "HotkeyToggleLockKey": 76,
              "HotkeyToggleLockModifiers": 6,
              "OnlyShowVramProcesses": false
            }
            """);

        var loaded = SettingsStore.Load(SettingsPath);

        Assert.Equal("game.exe", loaded.SelectedProcessName);
        Assert.Equal(new GpuLuid(63027, 0), loaded.SelectedGpu);
        Assert.Null(loaded.SelectedGpuName);
        Assert.False(loaded.OverlayVisible);
        Assert.False(loaded.OverlayLocked);
        Assert.Equal(120, loaded.OverlayX);
        Assert.Equal(-40, loaded.OverlayY);
        Assert.False(loaded.MinimizeToTray);
        Assert.False(loaded.CloseToTray);
        Assert.True(loaded.StartWithWindows);
        Assert.Equal(0x56u, loaded.HotkeyToggleOverlayKey);
        Assert.Equal(0x6u, loaded.HotkeyToggleOverlayModifiers);
        Assert.Equal(0x4Cu, loaded.HotkeyToggleLockKey);
        Assert.Equal(0x6u, loaded.HotkeyToggleLockModifiers);
        Assert.False(loaded.OnlyShowVramProcesses);
        Assert.False(File.Exists(BadPath));
    }

    private void WriteFile(string text)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, text);
    }

    private static void AssertIsDefault(AppSettings loaded)
    {
        var defaults = new AppSettings();
        Assert.Null(loaded.SelectedProcessName);
        Assert.Null(loaded.SelectedGpu);
        Assert.Null(loaded.SelectedGpuName);
        Assert.Equal(defaults.OverlayVisible, loaded.OverlayVisible);
        Assert.Equal(defaults.OverlayLocked, loaded.OverlayLocked);
        Assert.Equal(defaults.OverlayX, loaded.OverlayX);
        Assert.Equal(defaults.OverlayY, loaded.OverlayY);
        Assert.Equal(defaults.MinimizeToTray, loaded.MinimizeToTray);
        Assert.Equal(defaults.CloseToTray, loaded.CloseToTray);
        Assert.Equal(defaults.StartWithWindows, loaded.StartWithWindows);
        Assert.Equal(defaults.HotkeyToggleOverlayKey, loaded.HotkeyToggleOverlayKey);
        Assert.Equal(defaults.HotkeyToggleOverlayModifiers, loaded.HotkeyToggleOverlayModifiers);
        Assert.Equal(defaults.HotkeyToggleLockKey, loaded.HotkeyToggleLockKey);
        Assert.Equal(defaults.HotkeyToggleLockModifiers, loaded.HotkeyToggleLockModifiers);
        Assert.Equal(defaults.OnlyShowVramProcesses, loaded.OnlyShowVramProcesses);
    }
}
