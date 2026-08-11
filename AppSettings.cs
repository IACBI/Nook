using System.Text.Json;

namespace Nook;

internal sealed class AppSettings
{
    public string? SelectedProcessName { get; set; }
    public uint? SelectedGpuLowPart { get; set; }
    public uint? SelectedGpuHighPart { get; set; }
    public bool OverlayVisible { get; set; } = true;
    public bool OverlayLocked { get; set; } = true;
    public int OverlayX { get; set; } = int.MinValue;
    public int OverlayY { get; set; } = int.MinValue;
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public uint HotkeyToggleOverlayKey { get; set; } = 0x56; // V key
    public uint HotkeyToggleOverlayModifiers { get; set; } = 0x0002 | 0x0004; // Ctrl + Shift
    public uint HotkeyToggleLockKey { get; set; } = 0x4C; // L key
    public uint HotkeyToggleLockModifiers { get; set; } = 0x0002 | 0x0004; // Ctrl + Shift
    public bool OnlyShowVramProcesses { get; set; } = true;

    public GpuLuid? SelectedGpu => SelectedGpuLowPart.HasValue && SelectedGpuHighPart.HasValue
        ? new GpuLuid(SelectedGpuLowPart.Value, SelectedGpuHighPart.Value)
        : null;

    public void SetSelectedGpu(GpuLuid? luid)
    {
        SelectedGpuLowPart = luid?.LowPart;
        SelectedGpuHighPart = luid?.HighPart;
    }
}

internal static class SettingsStore
{
    private const long MaxSettingsFileBytes = 64 * 1024;
    private static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nook");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            var file = new FileInfo(FilePath);
            if (!file.Exists || file.Length > MaxSettingsFileBytes)
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            // A damaged or unreadable profile must never stop the application from starting.
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are optional; monitoring must never fail because a profile is read-only.
        }
    }
}
