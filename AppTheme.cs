using System.Drawing;
using Microsoft.Win32;

namespace Nook;

/// <summary>The main window's colours for the current Windows theme.</summary>
internal readonly record struct ThemePalette(Color Background, Color Surface, Color Elevated, Color Foreground, Color Muted, Color Border, Color Accent);

/// <summary>
/// Light, dark and high-contrast styling for the main window. Controls opt into a role by their
/// <see cref="Control.Tag"/>; everything else is styled by its type.
/// </summary>
internal static class AppTheme
{
    public static readonly Color Accent = Color.FromArgb(0, 102, 204);
    public static readonly Color AccentPressed = Color.FromArgb(0, 78, 150);

    // Roles are tags, not colours: Apply overwrites colours itself, so it cannot read them back as markers.
    public const string MutedTag = "Muted";
    public const string AccentBadgeTag = "AccentBadge";
    public const string AccentTextTag = "AccentText";
    public const string MetricCardTag = "MetricCard";
    public const string ActionButtonTag = "ActionButton";
    public const string NavButtonTag = "NavButton";

    public static bool IsDarkMode()
    {
        if (SystemInformation.HighContrast) return false;
        return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int light && light == 0;
    }

    public static ThemePalette Current() =>
        SystemInformation.HighContrast
            ? new ThemePalette(SystemColors.Control, SystemColors.Window, SystemColors.Control, SystemColors.ControlText, SystemColors.GrayText, SystemColors.WindowFrame, SystemColors.Highlight)
            : IsDarkMode()
                ? new ThemePalette(Color.FromArgb(11, 15, 23), Color.FromArgb(22, 31, 48), Color.FromArgb(32, 43, 64), Color.FromArgb(248, 250, 252), Color.FromArgb(148, 163, 184), Color.FromArgb(51, 65, 85), Color.FromArgb(0, 180, 240))
                : new ThemePalette(Color.FromArgb(246, 248, 251), Color.White, Color.FromArgb(238, 242, 247), Color.FromArgb(23, 34, 49), Color.FromArgb(88, 104, 124), Color.FromArgb(196, 207, 222), Color.FromArgb(0, 120, 215));

    /// <summary>Styles every descendant of <paramref name="control"/> for <paramref name="colors"/>.</summary>
    public static void Apply(Control control, ThemePalette colors)
    {
        foreach (Control child in control.Controls)
        {
            if (child.Tag as string == AccentBadgeTag)
            {
                child.BackColor = colors.Accent;
                child.ForeColor = Color.White;
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
            else if (child.Tag as string == ActionButtonTag && child is Button button)
            {
                StyleActionButtonIdle(button, colors);
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
                child.ForeColor = child.Tag as string == MutedTag ? colors.Muted : colors.Foreground;
                child.BackColor = child.Parent?.Tag as string == MetricCardTag ? colors.Surface : colors.Background;
            }

            Apply(child, colors);
        }
    }

    /// <summary>
    /// The resting look of an action button. Used both by <see cref="Apply"/> and on mouse-leave, so
    /// hovering a button returns it to the theme's colours rather than to a separate hard-coded set.
    /// </summary>
    public static void StyleActionButtonIdle(Button button, ThemePalette colors)
    {
        button.BackColor = colors.Elevated;
        button.ForeColor = colors.Foreground;
        button.FlatAppearance.BorderColor = colors.Border;
    }

    public static void StyleNavButton(Button button, bool isActive)
    {
        var dark = IsDarkMode();
        button.FlatAppearance.BorderSize = 0;
        if (isActive)
        {
            button.BackColor = Accent;
            button.ForeColor = Color.White;
        }
        else
        {
            button.BackColor = dark ? Color.FromArgb(36, 42, 52) : Color.FromArgb(232, 236, 242);
            button.ForeColor = dark ? Color.FromArgb(220, 228, 240) : Color.FromArgb(30, 40, 55);
        }
    }
}
