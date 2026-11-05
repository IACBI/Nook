using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Nook;

/// <summary>
/// A button that paints itself from theme colours. The dark-mode button renderer draws its own light
/// outline over any FlatAppearance setting, so the look is drawn here; behaviour (keyboard, focus,
/// accessibility) stays the stock <see cref="Button"/>'s.
/// </summary>
internal sealed class ThemedButton : Button
{
    private const int Radius = 4;
    private bool _hover;
    private bool _pressed;

    public ThemedButton()
    {
        FlatStyle = FlatStyle.Flat;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColor { get; set; } = SystemColors.Control;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color HoverColor { get; set; } = SystemColors.ControlLight;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color PressedColor { get; set; } = SystemColors.ControlDark;

    /// <summary>Outline colour; <see cref="Color.Empty"/> draws none.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color OutlineColor { get; set; } = Color.Empty;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FocusColor { get; set; } = SystemColors.Highlight;

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var fill = !Enabled ? FillColor : _pressed ? PressedColor : _hover ? HoverColor : FillColor;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = GraphicsPaths.RoundedRectangle(bounds, Radius))
        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, path);
            if (!OutlineColor.IsEmpty)
            {
                using var pen = new Pen(OutlineColor);
                g.DrawPath(pen, path);
            }
        }

        // Only for keyboard focus, like the stock button: a ring after every click would be noise.
        if (Focused && ShowFocusCues)
        {
            var ring = Rectangle.Inflate(bounds, -2, -2);
            using var path = GraphicsPaths.RoundedRectangle(ring, Radius - 1);
            using var pen = new Pen(FocusColor, 2f);
            g.DrawPath(pen, path);
        }

        var text = Enabled ? ForeColor : SystemColors.GrayText;
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }

    /// <summary>Sets every colour at once and repaints.</summary>
    public void SetColors(Color fill, Color hover, Color pressed, Color text, Color outline, Color focus)
    {
        FillColor = fill;
        HoverColor = hover;
        PressedColor = pressed;
        OutlineColor = outline;
        FocusColor = focus;
        BackColor = fill;
        ForeColor = text;
        Invalidate();
    }
}
