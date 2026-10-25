using System.Drawing;
using System.Drawing.Drawing2D;

namespace Nook;

/// <summary>Dark styling for the tray and overlay-corner menus.</summary>
internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
{
    public DarkMenuRenderer() : base(new DarkColorTable()) { }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Color.Transparent;
        public override Color MenuItemSelectedGradientBegin => Color.Transparent;
        public override Color MenuItemSelectedGradientEnd => Color.Transparent;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuBorder => Color.FromArgb(51, 65, 85);
        public override Color ToolStripDropDownBackground => Color.FromArgb(17, 24, 39);
        public override Color ImageMarginGradientBegin => Color.FromArgb(17, 24, 39);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(17, 24, 39);
        public override Color ImageMarginGradientEnd => Color.FromArgb(17, 24, 39);
        public override Color SeparatorDark => Color.FromArgb(45, 55, 72);
        public override Color SeparatorLight => Color.Transparent;
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item.Selected && e.Item.Enabled)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(3, 1, e.Item.Width - 6, e.Item.Height - 2);
            using var path = GraphicsPaths.RoundedRectangle(rect, 4);
            using var brush = new SolidBrush(Color.FromArgb(40, 0, 225, 255));
            e.Graphics.FillPath(brush, path);
        }
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var checkRect = e.ImageRectangle;
        using var checkPen = new Pen(Color.FromArgb(0, 225, 255), 2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        var cx = checkRect.X + checkRect.Width / 2f;
        var cy = checkRect.Y + checkRect.Height / 2f;

        PointF p1 = new(cx - 4f, cy);
        PointF p2 = new(cx - 1f, cy + 3.5f);
        PointF p3 = new(cx + 5f, cy - 3.5f);

        e.Graphics.DrawLines(checkPen, [p1, p2, p3]);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var y = e.Item.Height / 2;
        using var pen = new Pen(Color.FromArgb(40, 255, 255, 255), 1f);
        e.Graphics.DrawLine(pen, 12, y, e.Item.Width - 12, y);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Selected ? Color.FromArgb(248, 250, 252) : Color.FromArgb(203, 213, 225);
        base.OnRenderItemText(e);
    }
}
