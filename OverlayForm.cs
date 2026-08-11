using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text;

namespace Nook;

internal sealed class OverlayForm : Form
{
    private const int WsExLayered = 0x00080000;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTransparent = 0x00000020;
    private const int GwlExStyle = -20;
    private const int SwpNoMove = 0x0002;
    private const int SwpNoSize = 0x0001;
    private const int SwpNoZOrder = 0x0004;
    private const int SwpFrameChanged = 0x0020;
    private const int WmMouseActivate = 0x0021;
    private const int WmNcHitTest = 0x0084;
    private const int MaNoActivate = 3;
    private const int HtTransparent = -1;
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;

    private const int PaddingX = 10;
    private const int PaddingY = 5;
    private const int MinWidth = 200;
    private const int MaxWidth = 580;
    private const TextFormatFlags MeasureFlags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

    private static readonly Size MaxTextSize = new(MaxWidth, 40);

    private static readonly Color HeadingColor = Color.FromArgb(248, 250, 252);
    private static readonly Color TempColor = Color.FromArgb(255, 200, 80);
    private static readonly Color ClockColor = Color.FromArgb(134, 239, 172);
    private static readonly Color MemoryColor = Color.FromArgb(0, 229, 255);

    private readonly Font _fontGpu = new("Segoe UI Semibold", 10f);
    private readonly Font _fontDetail = new("Segoe UI Semibold", 8.5f);
    private readonly Font _badgeFont = new("Segoe UI Semibold", 7f);
    private readonly List<OverlayRow> _rows = [];
    private Bitmap? _renderBitmap;
    private string _renderedSignature = string.Empty;

    private bool _locked = true;

    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(MinWidth, 90);
        DoubleBuffered = true;

        _rows.Add(new OverlayRow("GPU  •  —", _fontGpu, HeadingColor));
        _rows.Add(new OverlayRow("VRAM —", _fontDetail, MemoryColor));

        MouseDown += BeginDrag;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Locked
    {
        get => _locked;
        set
        {
            if (_locked == value)
            {
                return;
            }

            _locked = value;
            ApplyExtendedStyles();
            RenderOverlay(force: true);
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExLayered | WsExNoActivate | WsExToolWindow;
            if (_locked)
            {
                parameters.ExStyle |= WsExTransparent;
            }

            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RenderOverlay(force: true);
    }

    public void UpdateMetrics(string gpuName, string usage, string memoryLabel, string adapterMemory, string gpuTemp, string gpuClock, string? processName, string? processMemory)
    {
        _rows.Clear();
        _rows.Add(new OverlayRow($"{FormatShortGpuName(gpuName)}  •  {usage}", _fontGpu, HeadingColor));

        if (HasReading(gpuTemp))
        {
            _rows.Add(new OverlayRow($"TEMP   {gpuTemp}", _fontDetail, TempColor));
        }

        if (HasReading(gpuClock))
        {
            _rows.Add(new OverlayRow($"CLOCK   {gpuClock}", _fontDetail, ClockColor));
        }

        var memory = string.IsNullOrEmpty(processName)
            ? $"{memoryLabel}   {adapterMemory}"
            : $"{memoryLabel}   {adapterMemory}   •   {processName}  {processMemory ?? "—"}";
        _rows.Add(new OverlayRow(memory, _fontDetail, MemoryColor));

        RenderOverlay();
    }

    private static bool HasReading(string value) => !string.IsNullOrWhiteSpace(value) && value != "—";

    /// <summary>Trims vendor boilerplate so the model number fits the overlay.</summary>
    internal static string FormatShortGpuName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return "GPU";
        }

        var name = fullName
            .Replace("NVIDIA GeForce ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("NVIDIA ", "", StringComparison.OrdinalIgnoreCase)
            .Replace("AMD Radeon ", "Radeon ", StringComparison.OrdinalIgnoreCase)
            .Replace("Intel(R) ", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" Laptop GPU", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" Desktop GPU", "", StringComparison.OrdinalIgnoreCase);

        return name.Trim();
    }

    public void RenderOverlay(bool force = false)
    {
        if (!IsHandleCreated || _rows.Count == 0)
        {
            return;
        }

        var signature = BuildSignature();
        if (!force && signature == _renderedSignature)
        {
            return;
        }

        var contentWidth = 0;
        var contentHeight = 0;
        foreach (var row in _rows)
        {
            var size = TextRenderer.MeasureText(row.Text, row.Font, MaxTextSize, MeasureFlags);
            contentWidth = Math.Max(contentWidth, size.Width);
            contentHeight += RowHeight(row.Font);
        }

        var badgeExtra = _locked ? 0 : 54;
        var targetWidth = Math.Clamp(contentWidth + PaddingX * 2 + badgeExtra, MinWidth, MaxWidth);
        var targetHeight = contentHeight + PaddingY * 2;
        if (Width != targetWidth || Height != targetHeight)
        {
            Size = new Size(targetWidth, targetHeight);
        }

        if (_renderBitmap is null || _renderBitmap.Width != Width || _renderBitmap.Height != Height)
        {
            _renderBitmap?.Dispose();
            _renderBitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        }

        using (var g = Graphics.FromImage(_renderBitmap))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            DrawBackdrop(g);

            var y = PaddingY;
            foreach (var row in _rows)
            {
                DrawSmoothShadowText(g, row, new Point(PaddingX, y));
                y += RowHeight(row.Font);
            }
        }

        UpdateLayeredWindowBitmap(_renderBitmap);
        _renderedSignature = signature;
    }

    private void DrawBackdrop(Graphics g)
    {
        var rect = new Rectangle(1, 1, Width - 2, Height - 2);
        using var backdropPath = CreateRoundedRectanglePath(rect, 8);
        using (var backdropBrush = new SolidBrush(Color.FromArgb(170, 11, 15, 23)))
        {
            g.FillPath(backdropBrush, backdropPath);
        }

        if (_locked)
        {
            using var borderPen = new Pen(Color.FromArgb(40, 255, 255, 255), 1f);
            g.DrawPath(borderPen, backdropPath);
            return;
        }

        using (var borderPen = new Pen(Color.FromArgb(0, 180, 240), 1.5f) { DashStyle = DashStyle.Dash })
        {
            g.DrawPath(borderPen, backdropPath);
        }

        const string badgeText = "MOVE";
        var badgeSize = TextRenderer.MeasureText(badgeText, _badgeFont);
        var badgeRect = new Rectangle(Width - badgeSize.Width - 10, 4, badgeSize.Width + 4, badgeSize.Height + 1);
        using (var badgeBrush = new SolidBrush(Color.FromArgb(0, 140, 230)))
        {
            g.FillRectangle(badgeBrush, badgeRect);
        }

        TextRenderer.DrawText(g, badgeText, _badgeFont, badgeRect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private string BuildSignature()
    {
        var builder = new StringBuilder(_locked ? "L" : "U");
        foreach (var row in _rows)
        {
            builder.Append('\u001F').Append(row.Text);
        }

        return builder.ToString();
    }

    private static int RowHeight(Font font) => font.Height + 2;

    private static void DrawSmoothShadowText(Graphics g, OverlayRow row, Point location)
    {
        var height = RowHeight(row.Font);
        var shadowRect = new Rectangle(location.X + 1, location.Y + 1, MaxTextSize.Width, height);
        var textRect = new Rectangle(location.X, location.Y, MaxTextSize.Width, height);

        TextRenderer.DrawText(g, row.Text, row.Font, shadowRect, Color.FromArgb(160, 0, 0, 0), MeasureFlags);
        TextRenderer.DrawText(g, row.Text, row.Font, textRect, row.Color, MeasureFlags);
    }

    private void UpdateLayeredWindowBitmap(Bitmap bitmap)
    {
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr memDc = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;

        try
        {
            hBitmap = bitmap.GetHbitmap(Color.FromArgb(0, 0, 0, 0));
            if (hBitmap == IntPtr.Zero) return;

            memDc = CreateCompatibleDC(IntPtr.Zero);
            if (memDc == IntPtr.Zero) return;

            oldBitmap = SelectObject(memDc, hBitmap);
            if (oldBitmap == IntPtr.Zero) return;

            var size = new SizeNative { Width = bitmap.Width, Height = bitmap.Height };
            var pointSource = new PointNative { X = 0, Y = 0 };
            var topPos = new PointNative { X = Left, Y = Top };

            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AC_SRC_ALPHA
            };

            UpdateLayeredWindow(Handle, IntPtr.Zero, ref topPos, ref size, memDc, ref pointSource, 0, ref blend, ULW_ALPHA);
        }
        finally
        {
            if (memDc != IntPtr.Zero)
            {
                if (oldBitmap != IntPtr.Zero)
                {
                    SelectObject(memDc, oldBitmap);
                }
                DeleteDC(memDc);
            }
            if (hBitmap != IntPtr.Zero)
            {
                DeleteObject(hBitmap);
            }
        }
    }

    private static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        var arc = new Rectangle(rect.X, rect.Y, diameter, diameter);

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

    public void MoveToCorner(OverlayCorner corner)
    {
        // Render first: the window resizes itself to fit the current rows, and the
        // bottom/right corners depend on the final size.
        RenderOverlay(force: true);

        var area = Screen.FromControl(this).WorkingArea;
        Location = corner switch
        {
            OverlayCorner.TopLeft => new Point(area.Left + 12, area.Top + 12),
            OverlayCorner.TopRight => new Point(area.Right - Width - 12, area.Top + 12),
            OverlayCorner.BottomLeft => new Point(area.Left + 12, area.Bottom - Height - 12),
            _ => new Point(area.Right - Width - 12, area.Bottom - Height - 12)
        };
    }

    public void RestoreLocation(Point location)
    {
        var bounds = new Rectangle(location, Size);
        var screen = Screen.AllScreens.FirstOrDefault(candidate => candidate.WorkingArea.IntersectsWith(bounds)) ?? Screen.PrimaryScreen;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        Location = new Point(
            Math.Clamp(location.X, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(location.Y, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        RenderOverlay(force: true);
    }

    protected override void WndProc(ref Message m)
    {
        if (_locked && m.Msg == WmNcHitTest)
        {
            m.Result = (IntPtr)HtTransparent;
            return;
        }

        if (m.Msg == WmMouseActivate)
        {
            m.Result = (IntPtr)MaNoActivate;
            return;
        }

        base.WndProc(ref m);
    }

    private void BeginDrag(object? sender, MouseEventArgs e)
    {
        if (_locked || e.Button != MouseButtons.Left)
        {
            return;
        }

        ReleaseCapture();
        SendMessageW(Handle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
    }

    private void ApplyExtendedStyles()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        var styles = GetWindowLongPtrW(Handle, GwlExStyle).ToInt64();
        styles |= WsExLayered | WsExNoActivate | WsExToolWindow;
        styles = _locked ? styles | WsExTransparent : styles & ~WsExTransparent;
        SetWindowLongPtrW(Handle, GwlExStyle, (IntPtr)styles);
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fontGpu.Dispose();
            _fontDetail.Dispose();
            _badgeFont.Dispose();
            _renderBitmap?.Dispose();
        }

        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointNative { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SizeNative { public int Width; public int Height; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;
    private const uint ULW_ALPHA = 0x02;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr hdcDst, ref PointNative pptDst, ref SizeNative psize,
        IntPtr hdcSrc, ref PointNative pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool ReleaseCapture();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern IntPtr SendMessageW(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", ExactSpelling = true)]
    private static extern IntPtr GetWindowLongPtrW(IntPtr window, int index);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", ExactSpelling = true)]
    private static extern IntPtr SetWindowLongPtrW(IntPtr window, int index, IntPtr value);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, int flags);
}

internal readonly record struct OverlayRow(string Text, Font Font, Color Color);

internal enum OverlayCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}
