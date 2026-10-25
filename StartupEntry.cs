using Microsoft.Win32;

namespace Nook;

/// <summary>The per-user Run key value that starts Nook with Windows.</summary>
internal static class StartupEntry
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Nook";

    /// <summary>Adds or removes the entry. Returns a message for the user when it fails, or null.</summary>
    public static string? Update(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
            if (key is null)
            {
                return "The Windows startup entry could not be opened.";
            }

            if (enable)
            {
                // Unquoted paths containing spaces let Windows launch a neighbouring
                // executable instead — quote the value even though ours rarely needs it.
                key.SetValue(ValueName, $"\"{Environment.ProcessPath ?? Application.ExecutablePath}\"");
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }

            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return $"The Windows startup entry could not be updated: {ex.Message}";
        }
    }
}
