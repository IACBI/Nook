using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nook;

internal sealed class AppSettings
{
    public string? SelectedProcessName { get; set; }
    public uint? SelectedGpuLowPart { get; set; }
    public uint? SelectedGpuHighPart { get; set; }
    /// <summary>Adapter name, used when the LUID no longer matches after a reboot.</summary>
    public string? SelectedGpuName { get; set; }
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

    [JsonIgnore]
    public GpuLuid? SelectedGpu => SelectedGpuLowPart.HasValue && SelectedGpuHighPart.HasValue
        ? new GpuLuid(SelectedGpuLowPart.Value, SelectedGpuHighPart.Value)
        : null;

    /// <summary>Stores the selected adapter's LUID and name.</summary>
    public void SetSelectedGpu(GpuLuid? luid, string? name)
    {
        SetSelectedGpu(luid);
        SelectedGpuName = name;
    }

    /// <summary>Stores only the LUID; the remembered adapter name is left unchanged.</summary>
    public void SetSelectedGpu(GpuLuid? luid)
    {
        SelectedGpuLowPart = luid?.LowPart;
        SelectedGpuHighPart = luid?.HighPart;
    }
}

internal static class SettingsStore
{
    private const long MaxSettingsFileBytes = 64 * 1024;
    private const uint MinVirtualKey = 0x01;
    private const uint MaxVirtualKey = 0xFE;
    private const uint ModifierKeysMask = 0x0001 | 0x0002 | 0x0004 | 0x0008; // MOD_ALT, MOD_CONTROL, MOD_SHIFT, MOD_WIN
    private const uint ValidModifiersMask = ModifierKeysMask | 0x4000; // plus MOD_NOREPEAT
    private static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nook");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>
    /// Why the last <see cref="Load()"/> fell back to defaults, or null when the profile loaded or did not
    /// exist. The store has no logger of its own, so the caller reports it.
    /// </summary>
    public static string? LastLoadProblem { get; private set; }

    public static AppSettings Load()
    {
        var settings = Load(FilePath, out var problem);
        LastLoadProblem = problem;
        return settings;
    }

    public static bool Save(AppSettings settings) => Save(settings, FilePath);

    internal static AppSettings Load(string filePath) => Load(filePath, out _);

    /// <summary>
    /// Reads the profile at <paramref name="filePath"/>. Each field is read on its own, so a damaged
    /// field falls back to its default without discarding the rest. A file that cannot be used at all
    /// is copied to settings.bad.json first, so the next save does not silently replace it.
    /// </summary>
    internal static AppSettings Load(string filePath, out string? problem)
    {
        problem = null;
        try
        {
            var file = new FileInfo(filePath);
            if (!file.Exists)
            {
                return new AppSettings();
            }

            if (file.Length > MaxSettingsFileBytes)
            {
                problem = KeepUnusableCopy(filePath, "is larger than 64 KB");
                return new AppSettings();
            }

            using var document = JsonDocument.Parse(File.ReadAllText(filePath));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                problem = KeepUnusableCopy(filePath, "is not a JSON object");
                return new AppSettings();
            }

            return ReadProfile(document.RootElement);
        }
        catch (JsonException ex)
        {
            problem = KeepUnusableCopy(filePath, $"is not valid JSON ({ex.Message})");
            return new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            // A damaged or unreadable profile must never stop the application from starting.
            problem = $"Settings could not be read ({ex.Message}); defaults are in use.";
            return new AppSettings();
        }
    }

    /// <summary>
    /// Writes the profile through a temporary file and then moves it into place, so a crash mid-write
    /// leaves the previous profile intact. Returns false instead of throwing on I/O or permission errors.
    /// </summary>
    internal static bool Save(AppSettings settings, string filePath)
    {
        var tempPath = filePath + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(JsonSerializer.SerializeToUtf8Bytes(settings, Options));
                // Flush before the rename so a power loss cannot leave an empty profile in place.
                stream.Flush(true);
            }

            File.Move(tempPath, filePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are optional; monitoring must never fail because a profile is read-only.
            DeleteTemporaryFile(tempPath);
            return false;
        }
    }

    private static AppSettings ReadProfile(JsonElement root)
    {
        var defaults = new AppSettings();
        var settings = new AppSettings
        {
            SelectedProcessName = ReadString(root, nameof(AppSettings.SelectedProcessName), defaults.SelectedProcessName),
            SelectedGpuLowPart = ReadNullableUInt32(root, nameof(AppSettings.SelectedGpuLowPart), defaults.SelectedGpuLowPart),
            SelectedGpuHighPart = ReadNullableUInt32(root, nameof(AppSettings.SelectedGpuHighPart), defaults.SelectedGpuHighPart),
            SelectedGpuName = ReadString(root, nameof(AppSettings.SelectedGpuName), defaults.SelectedGpuName),
            OverlayVisible = ReadBool(root, nameof(AppSettings.OverlayVisible), defaults.OverlayVisible),
            OverlayLocked = ReadBool(root, nameof(AppSettings.OverlayLocked), defaults.OverlayLocked),
            OverlayX = ReadInt32(root, nameof(AppSettings.OverlayX), defaults.OverlayX),
            OverlayY = ReadInt32(root, nameof(AppSettings.OverlayY), defaults.OverlayY),
            MinimizeToTray = ReadBool(root, nameof(AppSettings.MinimizeToTray), defaults.MinimizeToTray),
            CloseToTray = ReadBool(root, nameof(AppSettings.CloseToTray), defaults.CloseToTray),
            StartWithWindows = ReadBool(root, nameof(AppSettings.StartWithWindows), defaults.StartWithWindows),
            OnlyShowVramProcesses = ReadBool(root, nameof(AppSettings.OnlyShowVramProcesses), defaults.OnlyShowVramProcesses),
        };

        // A hotkey's key and modifiers are only meaningful together, so they fall back as a pair.
        (settings.HotkeyToggleOverlayKey, settings.HotkeyToggleOverlayModifiers) = ReadHotkey(
            root,
            nameof(AppSettings.HotkeyToggleOverlayKey),
            nameof(AppSettings.HotkeyToggleOverlayModifiers),
            defaults.HotkeyToggleOverlayKey,
            defaults.HotkeyToggleOverlayModifiers);
        (settings.HotkeyToggleLockKey, settings.HotkeyToggleLockModifiers) = ReadHotkey(
            root,
            nameof(AppSettings.HotkeyToggleLockKey),
            nameof(AppSettings.HotkeyToggleLockModifiers),
            defaults.HotkeyToggleLockKey,
            defaults.HotkeyToggleLockModifiers);

        return settings;
    }

    private static (uint Key, uint Modifiers) ReadHotkey(JsonElement root, string keyName, string modifiersName, uint defaultKey, uint defaultModifiers)
    {
        var key = ReadUInt32(root, keyName, defaultKey);
        var modifiers = ReadUInt32(root, modifiersName, defaultModifiers);
        return IsValidHotkey(key, modifiers) ? (key, modifiers) : (defaultKey, defaultModifiers);
    }

    private static bool IsValidHotkey(uint key, uint modifiers) =>
        key is >= MinVirtualKey and <= MaxVirtualKey
        && (modifiers & ~ValidModifiersMask) == 0
        && (modifiers & ModifierKeysMask) != 0;

    private static string? ReadString(JsonElement root, string name, string? fallback) =>
        root.TryGetProperty(name, out var value) && (value.ValueKind is JsonValueKind.String or JsonValueKind.Null)
            ? value.GetString()
            : fallback;

    private static bool ReadBool(JsonElement root, string name, bool fallback) =>
        root.TryGetProperty(name, out var value) && (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            ? value.GetBoolean()
            : fallback;

    private static int ReadInt32(JsonElement root, string name, int fallback) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : fallback;

    private static uint ReadUInt32(JsonElement root, string name, uint fallback) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out var number)
            ? number
            : fallback;

    private static uint? ReadNullableUInt32(JsonElement root, string name, uint? fallback)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return fallback;
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out var number) ? number : fallback;
    }

    /// <summary>
    /// Copies an unusable profile to settings.bad.json so the next save cannot destroy it, and
    /// returns the message that explains what happened.
    /// </summary>
    private static string KeepUnusableCopy(string filePath, string reason)
    {
        try
        {
            File.Copy(filePath, Path.Combine(Path.GetDirectoryName(filePath) ?? string.Empty, "settings.bad.json"), overwrite: true);
            return $"settings.json {reason}; it was kept as settings.bad.json and defaults are in use.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Best effort: the defaults remain usable without the preserved copy.
            return $"settings.json {reason} and could not be kept as settings.bad.json ({ex.Message}); defaults are in use.";
        }
    }

    private static void DeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover temporary file is overwritten by the next save.
        }
    }
}
