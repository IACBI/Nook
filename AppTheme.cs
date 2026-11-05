using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Nook;

/// <summary>The main window's colours for the current Windows theme.</summary>
/// <param name="Accent">Accent for text drawn on <paramref name="Background"/> or <paramref name="Surface"/>; meets 4.5:1 on both.</param>
/// <param name="AccentFill">Accent for filled controls, with <paramref name="AccentFillText"/> on top.</param>
/// <param name="AccentSubtle">A faint accent tint for badges, with <paramref name="Accent"/> text on top.</param>
/// <param name="Hover">Background of a neutral button under the pointer.</param>
/// <param name="Pressed">Background of a neutral button while it is held down.</param>
internal readonly record struct ThemePalette(
    Color Background,
    Color Surface,
    Color Elevated,
    Color Foreground,
    Color Muted,
    Color Border,
    Color Accent,
    Color AccentFill,
    Color AccentFillText,
    Color AccentSubtle,
    Color Hover,
    Color Pressed,
    bool IsDark);

/// <summary>
/// Light, dark and high-contrast styling for the main window. Controls opt into a role by their
/// <see cref="Control.Tag"/>; everything else is styled by its type.
/// </summary>
internal static class AppTheme
{
    // Roles are tags, not colours: Apply overwrites colours itself, so it cannot read them back as markers.
    public const string MutedTag = "Muted";
    public const string AccentBadgeTag = "AccentBadge";
    public const string AccentTextTag = "AccentText";
    public const string MetricCardTag = "MetricCard";
    public const string ActionButtonTag = "ActionButton";
    public const string NavButtonTag = "NavButton";

    private static readonly ThemePalette Dark = new(
        Background: Color.FromArgb(11, 15, 23),
        Surface: Color.FromArgb(22, 31, 48),
        Elevated: Color.FromArgb(32, 43, 64),
        Foreground: Color.FromArgb(248, 250, 252),
        Muted: Color.FromArgb(148, 163, 184),
        Border: Color.FromArgb(51, 65, 85),
        Accent: Color.FromArgb(0, 180, 240),
        AccentFill: Color.FromArgb(0, 102, 204),
        AccentFillText: Color.White,
        AccentSubtle: Color.FromArgb(9, 45, 62),
        Hover: Color.FromArgb(44, 58, 84),
        Pressed: Color.FromArgb(26, 35, 52),
        IsDark: true);

    private static readonly ThemePalette Light = new(
        Background: Color.FromArgb(246, 248, 251),
        Surface: Color.White,
        Elevated: Color.FromArgb(232, 237, 244),
        Foreground: Color.FromArgb(23, 34, 49),
        Muted: Color.FromArgb(88, 104, 124),
        Border: Color.FromArgb(196, 207, 222),
        Accent: Color.FromArgb(0, 95, 184),
        AccentFill: Color.FromArgb(0, 95, 184),
        AccentFillText: Color.White,
        AccentSubtle: Color.FromArgb(221, 233, 244),
        Hover: Color.FromArgb(220, 228, 239),
        Pressed: Color.FromArgb(206, 217, 231),
        IsDark: false);

    public static bool IsDarkMode()
    {
        if (SystemInformation.HighContrast) return false;
        return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int light && light == 0;
    }

    public static ThemePalette Current() =>
        SystemInformation.HighContrast
            ? new ThemePalette(
                SystemColors.Control, SystemColors.Window, SystemColors.Control, SystemColors.ControlText, SystemColors.GrayText,
                SystemColors.WindowFrame, SystemColors.HotTrack, SystemColors.Highlight, SystemColors.HighlightText, SystemColors.Window,
                SystemColors.Highlight, SystemColors.Highlight, IsDark: false)
            : IsDarkMode() ? Dark : Light;

    /// <summary>Styles every descendant of <paramref name="control"/> for <paramref name="colors"/>.</summary>
    public static void Apply(Control control, ThemePalette colors)
    {
        foreach (Control child in control.Controls)
        {
            if (child.Tag as string == AccentBadgeTag)
            {
                child.BackColor = colors.AccentSubtle;
                child.ForeColor = colors.Accent;
            }
            else if (child.Tag as string == AccentTextTag)
            {
                child.ForeColor = colors.Accent;
                child.BackColor = colors.Background;
            }
            else if (child.Tag as string == MetricCardTag)
            {
                child.BackColor = colors.Surface;
                child.ForeColor = colors.Foreground;
            }
            else if (child.Tag as string == ActionButtonTag && child is ThemedButton button)
            {
                StyleActionButton(button, colors);
            }
            else if (child is Panel or TableLayoutPanel or FlowLayoutPanel)
            {
                child.BackColor = colors.Background;
                child.ForeColor = colors.Foreground;
            }
            else if (child is ComboBox combo)
            {
                combo.BackColor = colors.Surface;
                combo.ForeColor = colors.Foreground;
            }
            else if (child is TextBox text)
            {
                text.BackColor = colors.Surface;
                text.ForeColor = colors.Foreground;
            }
            else if (child is Label or CheckBox)
            {
                var onCard = child.Parent?.Tag as string == MetricCardTag;
                child.ForeColor = child.Tag as string == MutedTag ? colors.Muted : colors.Foreground;
                child.BackColor = onCard ? colors.Surface : colors.Background;
            }

            Apply(child, colors);
        }
    }

    /// <summary>A neutral, outlined button for secondary commands such as Refresh.</summary>
    public static void StyleActionButton(ThemedButton button, ThemePalette colors) =>
        button.SetColors(colors.Elevated, colors.Hover, colors.Pressed, colors.Foreground, colors.Border, colors.Accent);

    /// <summary>
    /// One segment of the view switcher: the current view is filled with the accent, the others are
    /// neutral. In high contrast every segment keeps an outline, since fills alone may not differ.
    /// </summary>
    public static void StyleNavButton(ThemedButton button, bool isActive, ThemePalette colors)
    {
        var outline = SystemInformation.HighContrast ? colors.Border : Color.Empty;
        if (isActive)
        {
            button.SetColors(colors.AccentFill, colors.AccentFill, colors.AccentFill, colors.AccentFillText, outline, colors.AccentFillText);
        }
        else
        {
            button.SetColors(colors.Elevated, colors.Hover, colors.Pressed, colors.Foreground, outline, colors.Accent);
        }
    }

    /// <summary>
    /// Matches the title bar to the window body. Without it a dark window keeps a white caption.
    /// Older Windows 10 builds ignore the attribute, which leaves the default caption.
    /// </summary>
    public static void ApplyTitleBar(Form form, bool dark)
    {
        if (!form.IsHandleCreated) return;
        var value = dark ? 1 : 0;
        if (DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
        {
            // Windows 10 before 20H1 used an undocumented attribute number for the same thing.
            DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkModeBefore20H1, ref value, sizeof(int));
        }
    }

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
