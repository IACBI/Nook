using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Nook;

/// <summary>
/// Draws the application icon. The artwork lives here rather than in a file so the
/// window, taskbar and tray icons can never fall out of sync with the build; app.ico
/// in the repository root is exported from this code and only supplies the icon
/// resource Explorer reads off the executable.
/// </summary>
internal static class AppIconGenerator
{
    private static Icon? _cachedIcon;

    public static Icon GetAppIcon() => _cachedIcon ??= GenerateIcon();

    public static Icon GenerateIcon()
    {
        int[] sizes = [16, 32, 48, 64, 128, 256];
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write((ushort)0); // Reserved
        writer.Write((ushort)1); // Type ICO
        writer.Write((ushort)sizes.Length); // Image count

        var pngBuffers = new List<byte[]>();
        foreach (var size in sizes)
        {
            using var bmp = CreateIconBitmap(size);
            using var pngMs = new MemoryStream();
            bmp.Save(pngMs, ImageFormat.Png);
            pngBuffers.Add(pngMs.ToArray());
        }

        var offset = 6 + (sizes.Length * 16);
        for (var i = 0; i < sizes.Length; i++)
        {
            var size = sizes[i];
            var buffer = pngBuffers[i];

            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0); // Color count
            writer.Write((byte)0); // Reserved
            writer.Write((ushort)1); // Planes
            writer.Write((ushort)32); // Bit count
            writer.Write((uint)buffer.Length); // Bytes size
            writer.Write((uint)offset); // Offset

            offset += buffer.Length;
        }

        foreach (var buffer in pngBuffers)
        {
            writer.Write(buffer);
        }

        ms.Position = 0;
        return new Icon(ms);
    }

    private static Bitmap CreateIconBitmap(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // 1. Full-Bleed Modern Squircle Badge (Fills 95% of icon canvas)
        var margin = size <= 32 ? 0.5f : Math.Max(1f, size / 32f);
        var rect = new RectangleF(margin, margin, size - margin * 2f, size - margin * 2f);
        var radius = Math.Max(3f, size * 0.22f);

        using (var bgPath = CreateRoundedPathF(rect, radius))
        using (var bgBrush = new LinearGradientBrush(rect, Color.FromArgb(11, 15, 23), Color.FromArgb(22, 31, 48), 45f))
        {
            g.FillPath(bgBrush, bgPath);

            using var borderPen = new Pen(Color.FromArgb(0, 225, 255), Math.Max(1.2f, size / 24f));
            g.DrawPath(borderPen, bgPath);
        }

        // 2. Ultra-Crisp Central GPU "V" Monogram
        var center = size / 2f;
        var vWidth = size * 0.28f;
        var vHeight = size * 0.24f;
        var vTopY = center - vHeight * 0.45f;
        var vBottomY = center + vHeight * 0.65f;

        using (var vPen = new Pen(Color.FromArgb(0, 225, 255), Math.Max(2.5f, size / 9f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        })
        {
            PointF p1 = new(center - vWidth, vTopY);
            PointF p2 = new(center, vBottomY);
            PointF p3 = new(center + vWidth, vTopY);
            g.DrawLines(vPen, [p1, p2, p3]);
        }

        // Inner glowing white core
        using (var innerPen = new Pen(Color.FromArgb(248, 250, 252), Math.Max(1.2f, size / 16f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        })
        {
            PointF p1 = new(center - vWidth, vTopY);
            PointF p2 = new(center, vBottomY);
            PointF p3 = new(center + vWidth, vTopY);
            g.DrawLines(innerPen, [p1, p2, p3]);
        }

        return bmp;
    }

    private static GraphicsPath CreateRoundedPathF(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2f;
        var arc = new RectangleF(rect.X, rect.Y, diameter, diameter);

        path.AddArc(arc, 180, 90);
        arc.X = rect.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = rect.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = rect.X;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}
