using System.Runtime.InteropServices;

namespace Nook;

/// <summary>System-wide shortcuts registered against a window handle with RegisterHotKey.</summary>
internal static class GlobalHotkeys
{
    public const int WmHotKey = 0x0312;

    /// <summary>Returns false when another application already holds the combination.</summary>
    public static bool Register(IntPtr window, int id, uint modifiers, uint virtualKey) => RegisterHotKey(window, id, modifiers, virtualKey);

    public static void Unregister(IntPtr window, int id) => UnregisterHotKey(window, id);

    /// <summary>Formats a RegisterHotKey modifier mask and virtual key the way Windows menus show them.</summary>
    public static string Format(uint modifiers, uint virtualKey)
    {
        var parts = new List<string>(5);
        if ((modifiers & 0x2) != 0) parts.Add("Ctrl");
        if ((modifiers & 0x1) != 0) parts.Add("Alt");
        if ((modifiers & 0x4) != 0) parts.Add("Shift");
        if ((modifiers & 0x8) != 0) parts.Add("Win");
        var key = (Keys)virtualKey;
        parts.Add(key is >= Keys.D0 and <= Keys.D9 ? ((char)virtualKey).ToString() : key.ToString());
        return string.Join("+", parts);
    }

    /// <summary>
    /// Two shortcuts for the narrow bottom bar: "Ctrl+Shift+V / L" when they share their modifiers,
    /// both in full otherwise.
    /// </summary>
    public static string FormatPair(uint firstModifiers, uint firstKey, uint secondModifiers, uint secondKey) =>
        firstModifiers == secondModifiers
            ? $"{Format(firstModifiers, firstKey)} / {Format(0, secondKey)}"
            : $"{Format(firstModifiers, firstKey)} / {Format(secondModifiers, secondKey)}";

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
