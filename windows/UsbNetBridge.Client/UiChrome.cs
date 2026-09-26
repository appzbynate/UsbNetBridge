using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace UsbNetBridge.Client;

/// <summary>Shared premium chrome helpers for the Windows client.</summary>
internal static class UiTheme
{
    public static readonly Color BgDeep = Color.FromArgb(10, 15, 29);
    public static readonly Color BgMid = Color.FromArgb(7, 11, 20);
    public static readonly Color CardFace = Color.FromArgb(14, 22, 36);
    public static readonly Color CardFaceLite = Color.FromArgb(20, 31, 50);
    public static readonly Color Accent = Color.FromArgb(0, 240, 255);
    public static readonly Color AccentHot = Color.FromArgb(0, 200, 255);
    public static readonly Color TextPrimary = Color.FromArgb(241, 245, 249);
    public static readonly Color TextMuted = Color.FromArgb(130, 155, 185);
    public static readonly Color OkGreen = Color.FromArgb(0, 229, 163);
    public static readonly Color Danger = Color.FromArgb(225, 29, 72);
    public static readonly Color InputBg = Color.FromArgb(7, 11, 18);
    public static readonly Color CardBorder = Color.FromArgb(60, 84, 115);
    public static readonly Color RowHover = Color.FromArgb(19, 29, 45);
    public static readonly Color RowSelected = Color.FromArgb(26, 40, 62);
    public static readonly Color ChipBg = Color.FromArgb(35, 0, 240, 255);
    public static readonly Color ChipText = Color.FromArgb(0, 240, 255);

    public const int CornerRadius = 22;
    public const int WellRadius = 18;
    public const int ListItemHeight = 38;

    public static void BindRoundRegion(Control control, int radius)
    {
        void Apply(object? sender, EventArgs? e)
        {
            if (control.Width < 8 || control.Height < 8)
                return;
            using var path = CreateRoundRect(new Rectangle(0, 0, control.Width, control.Height), radius);
            var next = new Region(path);
            var old = control.Region;
            control.Region = next;
            old?.Dispose();
        }

        control.SizeChanged -= Apply;
        control.SizeChanged += Apply;
        Apply(null, null);
    }

    public static Font TryFont(params (string Family, float Size, FontStyle Style)[] options)
    {
        foreach (var (family, size, style) in options)
        {
            try
            {
                var f = new Font(family, size, style, GraphicsUnit.Point);
                if (string.Equals(f.FontFamily.Name, family, StringComparison.OrdinalIgnoreCase) ||
                    family.StartsWith("Segoe UI", StringComparison.OrdinalIgnoreCase))
                    return f;
                f.Dispose();
            }
            catch
            {
                /* try next */
            }
        }

        return new Font("Segoe UI", options.Length > 0 ? options[0].Size : 12f, FontStyle.Regular, GraphicsUnit.Point);
    }

    /// <summary>Soft fading glow + a light 3D bevel — no hard drop shadow.</summary>
    public static void DrawGlowText(Graphics g, string text, Font font, Color color, RectangleF bounds,
        StringAlignment align = StringAlignment.Center, StringAlignment lineAlign = StringAlignment.Center,
        float glowSize = 10f, bool bevel = true)
    {
        if (string.IsNullOrWhiteSpace(text) || bounds.Width < 8 || bounds.Height < 8)
            return;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = align,
            LineAlignment = lineAlign,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };
        using var path = new GraphicsPath();
        var em = font.Size * g.DpiY / 72f;
        path.AddString(text, font.FontFamily, (int)font.Style, em, bounds, format);

        for (var w = glowSize; w >= 1.4f; w -= 1.5f)
        {
            var t = w / glowSize;
            var a = (int)(36 * t * t);
            if (a < 5)
                continue;
            using var pen = new Pen(Color.FromArgb(a, color), w)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            g.DrawPath(pen, path);
        }

        using (var dark = (GraphicsPath)path.Clone())
        {
            using var m = new Matrix();
            m.Translate(0.5f, 1.15f);
            dark.Transform(m);
            using var b = new SolidBrush(Color.FromArgb(bevel ? 70 : 0, 8, 16, 36));
            if (bevel)
                g.FillPath(b, dark);
        }

        using (var hi = (GraphicsPath)path.Clone())
        {
            using var m = new Matrix();
            m.Translate(-0.45f, -0.85f);
            hi.Transform(m);
            using var b = new SolidBrush(Color.FromArgb(bevel ? 80 : 40, 255, 255, 255));
            g.FillPath(b, hi);
        }

        var top = Color.FromArgb(255, Math.Min(255, color.R + 70), Math.Min(255, color.G + 40), Math.Min(255, color.B + 20));
        using var fill = new LinearGradientBrush(bounds, top, color, 90f);
        g.FillPath(fill, path);
        using var edge = new Pen(Color.FromArgb(90, 255, 255, 255), 0.75f)
        {
            LineJoin = LineJoin.Round,
        };
        g.DrawPath(edge, path);
    }

    public static GraphicsPath CreateRoundRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(1, radius * 2);
        var r = bounds;
        if (d > r.Width) d = Math.Max(1, r.Width);
        if (d > r.Height) d = Math.Max(1, r.Height);
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static int DrawChip(Graphics g, Rectangle bounds, string text, Color bg, Color fg, int rightInset = 0,
        bool batteryIcon = false)
    {
        if (string.IsNullOrWhiteSpace(text) || bounds.Width < 8 || bounds.Height < 8)
            return 0;
        using var font = string.Equals(text, "Connected", StringComparison.OrdinalIgnoreCase)
            ? new Font("Segoe UI", 8.5f, FontStyle.Bold)
            : new Font("Segoe UI Semibold", 8f);
        const TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                                      TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding |
                                      TextFormatFlags.GlyphOverhangPadding;
        var size = TextRenderer.MeasureText(text, font, Size.Empty, flags);
        var iconW = batteryIcon ? 14 : 0;
        var w = Math.Min(bounds.Width, size.Width + 18 + iconW);
        var h = Math.Min(bounds.Height, 24);
        var rect = new Rectangle(bounds.Right - w - rightInset, bounds.Y + (bounds.Height - h) / 2, w, h);
        using var path = CreateRoundRect(rect, h / 2);
        using var fill = new SolidBrush(bg);
        g.FillPath(fill, path);
        using var chipBorder = new Pen(Color.FromArgb(70, fg), 1f);
        g.DrawPath(chipBorder, path);
        var textRect = rect;
        if (batteryIcon)
        {
            DrawBatteryGlyph(g, rect, fg, text);
            textRect = new Rectangle(rect.X + 14, rect.Y, Math.Max(8, rect.Width - 16), rect.Height);
        }
        TextRenderer.DrawText(g, text, font, textRect, fg, flags);
        return w + 6;
    }

    private static void DrawBatteryGlyph(Graphics g, Rectangle chip, Color fg, string text)
    {
        var raw = text.Trim().TrimEnd('+').TrimEnd('%');
        _ = int.TryParse(raw, out var pct);
        pct = Math.Clamp(pct, 0, 100);
        var body = new Rectangle(chip.X + 6, chip.Y + (chip.Height - 10) / 2, 11, 10);
        using var pen = new Pen(fg, 1.2f);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawRectangle(pen, body);
        using var nubBrush = new SolidBrush(fg);
        g.FillRectangle(nubBrush, body.Right + 1, body.Y + 3, 2, 4);
        var inner = Rectangle.Inflate(body, -2, -2);
        var fillW = Math.Max(0, (int)Math.Round(inner.Width * (pct / 100.0)));
        if (fillW > 0)
        {
            using var fillBrush = new SolidBrush(fg);
            g.FillRectangle(fillBrush, inner.X, inner.Y, fillW, inner.Height);
        }
    }
}

internal static class UiAssets
{
    public static Image? LoadPng(string fileName)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, fileName);
            if (File.Exists(path))
                return Image.FromFile(path);
        }
        catch
        {
            /* ignore missing art */
        }

        return null;
    }

    public static void DrawCover(Graphics g, Image img, Rectangle dest)
    {
        if (dest.Width < 2 || dest.Height < 2)
            return;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var scale = Math.Max(dest.Width / (float)img.Width, dest.Height / (float)img.Height);
        var w = img.Width * scale;
        var h = img.Height * scale;
        var x = dest.X + (dest.Width - w) / 2f;
        var y = dest.Y + (dest.Height - h) / 2f;
        g.DrawImage(img, x, y, w, h);
    }
}

/// <summary>Top connect card scene — USB host to PC, matching the splash art.</summary>
internal sealed class ConnectingHero : Control
{
    private static Image? _circuitBg;
    private string _headline = "";
    private string _hint = "";
    private Color _headlineColor = UiTheme.Accent;

    public ConnectingHero()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
        BackColor = UiTheme.CardFace;
        AutoSize = true;
        _circuitBg ??= UiAssets.LoadPng("bg-circuit.png");
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var h = 16 + 54; // Top pad + capsuleH
        if (!string.IsNullOrWhiteSpace(_hint))
        {
            using var hintFont = new Font("Segoe UI", 9.25f);
            var maxW = Math.Max(20, proposedSize.Width - 28);
            if (maxW < 100) maxW = 400; // fallback if proposedSize is small/0
            var textH = TextRenderer.MeasureText(_hint, hintFont, new Size(maxW, 0), TextFormatFlags.WordBreak).Height;
            h += 10 + textH + 14; // gap + text height + bottom padding
        }
        else
        {
            h += 16; // bottom pad if no hint
        }
        return new Size(proposedSize.Width, h);
    }

    public string Headline
    {
        get => _headline;
        set
        {
            var next = value ?? "";
            if (string.Equals(_headline, next, StringComparison.Ordinal))
                return;
            _headline = next;
            Invalidate();
        }
    }

    public string Hint
    {
        get => _hint;
        set
        {
            var next = value ?? "";
            if (string.Equals(_hint, next, StringComparison.Ordinal))
                return;
            _hint = next;
            Invalidate();
        }
    }

    public Color HeadlineColor
    {
        get => _headlineColor;
        set
        {
            if (_headlineColor == value)
                return;
            _headlineColor = value;
            Invalidate();
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var r = ClientRectangle;
        if (r.Width < 8 || r.Height < 8)
            return;

        r.Width -= 1;
        r.Height -= 1;

        using (var fill = new SolidBrush(UiTheme.CardFace))
            g.FillRectangle(fill, r);

        if (_circuitBg != null && r.Width > 0 && r.Height > 0)
        {
            using var ia = new System.Drawing.Imaging.ImageAttributes();
            var matrix = new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.32f };
            ia.SetColorMatrix(matrix, System.Drawing.Imaging.ColorMatrixFlag.Default, System.Drawing.Imaging.ColorAdjustType.Bitmap);
            var scale = Math.Max(r.Width / (float)_circuitBg.Width, r.Height / (float)_circuitBg.Height);
            var cw = (int)Math.Ceiling(_circuitBg.Width * scale);
            var ch = (int)Math.Ceiling(_circuitBg.Height * scale);
            var cx0 = r.X + (r.Width - cw) / 2;
            var cy0 = r.Y + (r.Height - ch) / 2;
            var oldInterp = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(_circuitBg, new Rectangle(cx0, cy0, cw, ch), 0, 0, _circuitBg.Width, _circuitBg.Height, GraphicsUnit.Pixel, ia);
            g.InterpolationMode = oldInterp;
        }

        using var headlineFont = UiTheme.TryFont(
            ("Segoe UI", 14.5f, FontStyle.Bold),
            ("Segoe UI Semibold", 14f, FontStyle.Bold));

        var headText = string.IsNullOrWhiteSpace(_headline) ? "UsbNetBridge" : _headline;
        var headSize = TextRenderer.MeasureText(headText, headlineFont);
        var capsuleW = Math.Clamp(headSize.Width + 90, 240, Math.Max(240, r.Width - 48));
        var capsuleH = 54;
        var capsuleX = r.X + (r.Width - capsuleW) / 2;
        var capsuleY = r.Y + 16;
        var capsuleRect = new Rectangle(capsuleX, capsuleY, capsuleW, capsuleH);

        using (var capPath = UiTheme.CreateRoundRect(capsuleRect, 14))
        {
            var outerRect2 = Rectangle.Inflate(capsuleRect, 4, 4);
            using (var outerPath2 = UiTheme.CreateRoundRect(outerRect2, 18))
            using (var glowPen2 = new Pen(Color.FromArgb(28, UiTheme.Accent), 4.0f))
                g.DrawPath(glowPen2, outerPath2);

            var outerRect = Rectangle.Inflate(capsuleRect, 2, 2);
            using (var outerPath = UiTheme.CreateRoundRect(outerRect, 16))
            using (var glowPen = new Pen(Color.FromArgb(85, UiTheme.Accent), 2.5f))
                g.DrawPath(glowPen, outerPath);

            using (var capFill = new LinearGradientBrush(capsuleRect,
                       Color.FromArgb(12, 24, 43),
                       Color.FromArgb(8, 16, 30), 90f))
                g.FillPath(capFill, capPath);

            using var neonPen = new Pen(Color.FromArgb(245, UiTheme.Accent), 2.0f);
            g.DrawPath(neonPen, capPath);
        }

        var textRect = new RectangleF(capsuleX + 8, capsuleY + 6, capsuleW - 16, 24);
        UiTheme.DrawGlowText(g, headText, headlineFont, _headlineColor, textRect, glowSize: 7f, bevel: false);

        // Horizontal USB glyph matching the mockup precisely
        var glyphY = capsuleY + 36;
        var cx = capsuleX + capsuleW / 2f;
        using (var glyphPen = new Pen(UiTheme.Accent, 1.8f))
        {
            glyphPen.StartCap = LineCap.Round;
            glyphPen.EndCap = LineCap.Round;

            // Main stem line
            g.DrawLine(glyphPen, cx - 24, glyphY, cx + 16, glyphY);

            // Left circular node
            using var cyanBrush = new SolidBrush(UiTheme.Accent);
            g.FillEllipse(cyanBrush, cx - 30, glyphY - 3f, 6, 6);

            // Center-right branch to square node
            g.DrawLine(glyphPen, cx - 8, glyphY, cx + 4, glyphY - 6);
            g.FillRectangle(cyanBrush, cx + 3, glyphY - 9, 5, 5);

            // Right arrowhead pointing right
            var arrow = new[]
            {
                new PointF(cx + 24, glyphY),
                new PointF(cx + 16, glyphY - 4),
                new PointF(cx + 16, glyphY + 4)
            };
            g.FillPolygon(cyanBrush, arrow);
        }

        if (!string.IsNullOrWhiteSpace(_hint))
        {
            using var hintFont = new Font("Segoe UI", 9.25f);
            var maxW = Math.Max(20, r.Width - 28);
            var textH = TextRenderer.MeasureText(_hint, hintFont, new Size(maxW, 0), TextFormatFlags.WordBreak).Height;
            var hintRect = new Rectangle(14, capsuleY + capsuleH + 10, maxW, textH + 5);
            TextRenderer.DrawText(g, _hint, hintFont, hintRect,
                Color.FromArgb(148, 163, 184),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
        }

        using var borderPath = UiTheme.CreateRoundRect(r, 16);
        using var containerBorder = new Pen(UiTheme.CardBorder, 1f);
        g.DrawPath(containerBorder, borderPath);
    }
}

/// <summary>Section title with a fading cyan glow instead of a hard shadow.</summary>
internal sealed class SoftSectionTitle : Control
{
    private string _title = "";

    public SoftSectionTitle()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Dock = DockStyle.Fill;
    }

    public string Title
    {
        get => _title;
        set
        {
            var next = value ?? "";
            if (string.Equals(_title, next, StringComparison.Ordinal))
                return;
            _title = next;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (string.IsNullOrWhiteSpace(_title))
            return;
        using var font = UiTheme.TryFont(
            ("Segoe UI Semibold", 12f, FontStyle.Bold),
            ("Segoe UI", 12f, FontStyle.Bold));
        var r = ClientRectangle;
        TextRenderer.DrawText(g, _title, font, new Rectangle(r.X + 2, r.Y, r.Width - 4, r.Height),
            UiTheme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}

/// <summary>
/// Fully owner-painted list (not a native ListBox). Native list/richedit HWNDs
/// break WS_EX_COMPOSITED and flash independently while the parent is resized.
/// </summary>
internal sealed class SoftListBox : Control
{
    private readonly List<object> _items = new();
    private int _selectedIndex = -1;
    private int _suspendCount;
    private int _scrollY;

    public SoftListBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        TabStop = true;
        ItemHeight = UiTheme.ListItemHeight;
        BackColor = UiTheme.CardFace;
        ForeColor = UiTheme.TextPrimary;
        Font = new Font("Segoe UI", 10f);
        Items = new ItemList(this);
    }

    public ItemList Items { get; }
    private int _itemHeight;
    public int ItemHeight
    {
        get => _itemHeight;
        set
        {
            if (_itemHeight == value) return;
            _itemHeight = value;
            Invalidate();
        }
    }

    public object? SelectedItem =>
        _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var next = _items.Count == 0 ? -1 : Math.Clamp(value, -1, _items.Count - 1);
            if (next == _selectedIndex)
                return;
            _selectedIndex = next;
            EnsureSelectedVisible();
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? SelectedIndexChanged;

    public void BeginUpdate() => _suspendCount++;

    public void EndUpdate()
    {
        if (_suspendCount > 0)
            _suspendCount--;
        if (_suspendCount == 0)
            NotifyItemsChanged();
    }

    public int IndexFromPoint(Point p)
    {
        if (_items.Count == 0 || ItemHeight <= 0)
            return -1;
        var i = (p.Y + _scrollY) / ItemHeight;
        return i >= 0 && i < _items.Count ? i : -1;
    }

    internal void NotifyItemsChanged()
    {
        if (_suspendCount > 0)
            return;
        if (_selectedIndex >= _items.Count)
            SelectedIndex = _items.Count - 1;
        ClampScroll();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using (var bg = new SolidBrush(Parent?.BackColor ?? BackColor))
            g.FillRectangle(bg, ClientRectangle);

        if (_items.Count == 0 || ItemHeight <= 0)
            return;

        var first = Math.Max(0, _scrollY / ItemHeight);
        var last = Math.Min(_items.Count - 1, (_scrollY + Height) / ItemHeight);
        for (var i = first; i <= last; i++)
        {
            var bounds = new Rectangle(0, i * ItemHeight - _scrollY, Width, ItemHeight);
            DrawRow(g, bounds, i, i == _selectedIndex);
        }

        var totalHeight = _items.Count * ItemHeight;
        if (totalHeight > Height)
        {
            var thumbHeight = Math.Max(20, (int)((float)Height * Height / totalHeight));
            var maxScroll = totalHeight - Height;
            var scrollRatio = (float)_scrollY / maxScroll;
            var thumbY = (int)(scrollRatio * (Height - thumbHeight));
            var thumbRect = new Rectangle(Width - 6, thumbY + 2, 4, thumbHeight - 4);
            using (var path = UiTheme.CreateRoundRect(thumbRect, 2))
            using (var brush = new SolidBrush(Color.FromArgb(50, 255, 255, 255)))
                g.FillPath(brush, path);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        var idx = IndexFromPoint(e.Location);
        if (idx >= 0)
            SelectedIndex = idx;
        base.OnMouseDown(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        var delta = e.Delta > 0 ? -ItemHeight : ItemHeight;
        SetScroll(_scrollY + delta);
        base.OnMouseWheel(e);
    }

    protected override void OnResize(EventArgs e)
    {
        ClampScroll();
        base.OnResize(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End
        || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Up:
                SelectedIndex = Math.Max(0, SelectedIndex - 1);
                e.Handled = true;
                break;
            case Keys.Down:
                SelectedIndex = Math.Min(_items.Count - 1, Math.Max(0, SelectedIndex) + 1);
                e.Handled = true;
                break;
            case Keys.PageUp:
                SelectedIndex = Math.Max(0, SelectedIndex - Math.Max(1, Height / Math.Max(1, ItemHeight)));
                e.Handled = true;
                break;
            case Keys.PageDown:
                SelectedIndex = Math.Min(_items.Count - 1, SelectedIndex + Math.Max(1, Height / Math.Max(1, ItemHeight)));
                e.Handled = true;
                break;
            case Keys.Home:
                SelectedIndex = _items.Count == 0 ? -1 : 0;
                e.Handled = true;
                break;
            case Keys.End:
                SelectedIndex = _items.Count - 1;
                e.Handled = true;
                break;
        }
        base.OnKeyDown(e);
    }

    private void DrawRow(Graphics g, Rectangle bounds, int index, bool selected)
    {
        using (var bg = new SolidBrush(Parent?.BackColor ?? BackColor))
            g.FillRectangle(bg, bounds);

        var row = Rectangle.Inflate(bounds, -7, -4);
        if (row.Width < 8 || row.Height < 8)
            return;

        var isAttached = index >= 0 && index < _items.Count && _items[index] is AttachedUsbDevice;

        using (var path = UiTheme.CreateRoundRect(row, 10))
        {
            if (isAttached)
            {
                using var brush = new LinearGradientBrush(row,
                    Color.FromArgb(24, 0, 229, 255),
                    Color.FromArgb(10, 0, 160, 210), 90f);
                g.FillPath(brush, path);

                var outer2 = Rectangle.Inflate(row, 2, 2);
                using var glowPath2 = UiTheme.CreateRoundRect(outer2, 12);
                using var glow2 = new Pen(Color.FromArgb(25, UiTheme.Accent), 3f);
                g.DrawPath(glow2, glowPath2);

                var outer = Rectangle.Inflate(row, 1, 1);
                using var glowPath = UiTheme.CreateRoundRect(outer, 11);
                using var glow = new Pen(Color.FromArgb(60, UiTheme.Accent), 2f);
                g.DrawPath(glow, glowPath);

                using var border = new Pen(Color.FromArgb(240, UiTheme.Accent), 1.6f);
                g.DrawPath(border, path);
            }
            else if (selected)
            {
                using var brush = new LinearGradientBrush(row,
                    Color.FromArgb(45, 20, 50, 85),
                    Color.FromArgb(25, 14, 35, 60), 90f);
                g.FillPath(brush, path);

                using var border = new Pen(Color.FromArgb(160, UiTheme.Accent), 1.2f);
                g.DrawPath(border, path);

                var indicator = new Rectangle(row.X + 3, row.Y + 8, 3, Math.Max(4, row.Height - 16));
                using var indPath = UiTheme.CreateRoundRect(indicator, 2);
                using var indBrush = new SolidBrush(UiTheme.Accent);
                g.FillPath(indBrush, indPath);
            }
            else
            {
                using var fill = new SolidBrush(Color.FromArgb(16, 24, 38));
                g.FillPath(fill, path);
                using var border = new Pen(UiTheme.CardBorder, 1.5f);
                g.DrawPath(border, path);
            }
        }

        ResolveRow(_items[index], out var primary, out var secondary, out var chip, out var extraChip);

        var chipsInset = 0;
        if (!string.IsNullOrEmpty(chip))
            chipsInset += ChipWidth(chip);
        if (!string.IsNullOrEmpty(extraChip))
            chipsInset += ChipWidth(extraChip);
        var textLeft = row.X + 14;
        var textRightPad = chipsInset == 0 ? 14 : chipsInset + 10;
        var textWidth = Math.Max(20, row.Width - (textLeft - row.X) - textRightPad);
        var primaryRect = string.IsNullOrWhiteSpace(secondary)
            ? new Rectangle(textLeft, row.Y + Math.Max(0, (row.Height - 30) / 2), textWidth, 30)
            : new Rectangle(textLeft, row.Y + 6, textWidth, 30);
        var secondaryRect = new Rectangle(textLeft, row.Y + 34, textWidth, 20);

        using var primaryFont = new Font("Segoe UI Semibold", 10.5f);
        using var secondaryFont = new Font("Segoe UI", 9f);
        const TextFormatFlags rowText = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
                                        TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                                        TextFormatFlags.GlyphOverhangPadding;
        TextRenderer.DrawText(g, primary, primaryFont, primaryRect, Color.FromArgb(248, 250, 252), rowText);
        if (!string.IsNullOrWhiteSpace(secondary))
        {
            TextRenderer.DrawText(g, secondary, secondaryFont, secondaryRect, Color.FromArgb(138, 155, 181), rowText);
        }

        var used = 0;
        if (!string.IsNullOrEmpty(chip))
            used += UiTheme.DrawChip(g, new Rectangle(row.X, row.Y, row.Width - 12, row.Height),
                chip, ChipBg(chip), ChipFg(chip), used);
        if (!string.IsNullOrEmpty(extraChip))
            UiTheme.DrawChip(g, new Rectangle(row.X, row.Y, row.Width - 12, row.Height),
                extraChip, ChipBg(extraChip), ChipFg(extraChip), used,
                batteryIcon: _items[index] is OnlineServer);
    }

    private void EnsureSelectedVisible()
    {
        if (_selectedIndex < 0 || ItemHeight <= 0)
            return;
        var top = _selectedIndex * ItemHeight;
        var bottom = top + ItemHeight;
        if (top < _scrollY)
            SetScroll(top);
        else if (bottom > _scrollY + Height)
            SetScroll(bottom - Height);
    }

    private void SetScroll(int value)
    {
        var old = _scrollY;
        _scrollY = value;
        ClampScroll();
        if (_scrollY != old)
            Invalidate();
    }

    private void ClampScroll()
    {
        var max = Math.Max(0, _items.Count * ItemHeight - Height);
        _scrollY = Math.Clamp(_scrollY, 0, max);
    }

    private static bool LooksLikeBatteryChip(string text)
    {
        var raw = text.Trim().TrimEnd('+');
        return raw.EndsWith('%') && int.TryParse(raw[..^1], out _);
    }

    private static int ChipWidth(string text)
    {
        using var font = new Font("Segoe UI Semibold", 8.5f);
        const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding |
                                      TextFormatFlags.GlyphOverhangPadding;
        return TextRenderer.MeasureText(text, font, Size.Empty, flags).Width + 24 +
               (LooksLikeBatteryChip(text) ? 14 : 0);
    }

    private static Color ChipBg(string chip)
    {
        if (TryPingMs(chip, out var ms))
            return Color.FromArgb(70, PingQuality(ms));
        if (chip.Equals("Connected", StringComparison.OrdinalIgnoreCase))
            return UiTheme.Accent; // Solid glowing cyan pill matching mockup
        if (chip.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            return Color.FromArgb(30, 0, 229, 255);
        if (chip.Equals("Works well", StringComparison.OrdinalIgnoreCase))
            return Color.FromArgb(45, UiTheme.OkGreen);
        if (chip.Equals("timeout", StringComparison.OrdinalIgnoreCase) ||
            chip.Equals("Often fails", StringComparison.OrdinalIgnoreCase) ||
            chip.Equals("In use", StringComparison.OrdinalIgnoreCase))
            return Color.FromArgb(50, UiTheme.Danger);
        if (chip.Equals("Try it", StringComparison.OrdinalIgnoreCase))
            return Color.FromArgb(50, UiTheme.AccentHot);
        return UiTheme.ChipBg;
    }

    private static Color ChipFg(string chip)
    {
        if (TryPingMs(chip, out var ms))
            return PingQuality(ms);
        if (chip.Equals("Connected", StringComparison.OrdinalIgnoreCase))
            return Color.FromArgb(5, 17, 29); // Dark bold text on cyan pill matching mockup
        if (chip.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            return UiTheme.Accent;
        if (chip.Equals("Works well", StringComparison.OrdinalIgnoreCase))
            return Color.FromArgb(180, 255, 220);
        if (chip.Equals("timeout", StringComparison.OrdinalIgnoreCase) ||
            chip.Equals("Often fails", StringComparison.OrdinalIgnoreCase) ||
            chip.Equals("In use", StringComparison.OrdinalIgnoreCase))
            return Color.FromArgb(255, 190, 195);
        if (chip.Equals("Try it", StringComparison.OrdinalIgnoreCase))
            return Color.FromArgb(220, 225, 255);
        return UiTheme.ChipText;
    }

    private static bool TryPingMs(string chip, out long ms)
    {
        ms = 0;
        const string suffix = " ms";
        if (!chip.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return false;
        return long.TryParse(chip.AsSpan(0, chip.Length - suffix.Length).Trim(), out ms);
    }

    /// <summary>Green → yellow → orange → red from RTT. Timeout uses Danger separately.</summary>
    private static Color PingQuality(long ms) => ms switch
    {
        < 25 => Color.FromArgb(72, 220, 140),
        < 50 => Color.FromArgb(170, 220, 80),
        < 80 => Color.FromArgb(240, 190, 70),
        < 120 => Color.FromArgb(255, 140, 70),
        _ => Color.FromArgb(255, 110, 120),
    };

    private static void ResolveRow(object item, out string primary, out string secondary, out string? chip, out string? extraChip)
    {
        extraChip = null;
        secondary = ""; // Keep the UI minimalist by default
        switch (item)
        {
            case OnlineServer s:
                var showName = !string.IsNullOrWhiteSpace(s.Name) && !string.Equals(s.Name, s.Host, StringComparison.OrdinalIgnoreCase);
                primary = showName ? s.Name : s.Host;
                chip = s.StatusChip;
                extraChip = s.BatteryChip;
                return;
            case RemoteUsbDevice d:
                primary = d.DisplayName;
                chip = UsbClassHints.VerdictChip(d.ClassHint, d.Description);
                extraChip = d.AutoConnect ? "Auto" : null;
                return;
            case AttachedUsbDevice a:
                primary = a.DisplayName;
                chip = "Connected";
                extraChip = a.AutoConnect ? "Auto" : null;
                return;
            default:
                primary = item?.ToString() ?? "";
                chip = null;
                return;
        }
    }

    public sealed class ItemList : IEnumerable<object>
    {
        private readonly SoftListBox _owner;

        internal ItemList(SoftListBox owner) => _owner = owner;

        public int Count => _owner._items.Count;

        public object this[int index]
        {
            get => _owner._items[index];
            set
            {
                _owner._items[index] = value;
                _owner.Invalidate();
            }
        }

        public void Add(object item)
        {
            _owner._items.Add(item);
            _owner.NotifyItemsChanged();
        }

        public void Clear()
        {
            _owner._items.Clear();
            _owner._selectedIndex = -1;
            _owner._scrollY = 0;
            _owner.NotifyItemsChanged();
        }

        public IEnumerator<object> GetEnumerator() => _owner._items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

/// <summary>Rounded inset text field with cyan focus ring.</summary>
internal sealed class SoftTextWell : Panel
{
    private readonly TextBox _box;
    private bool _focused;

    public TextBox Inner => _box;

    public SoftTextWell(TextBox box)
    {
        _box = box;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
        Padding = new Padding(12, 8, 12, 8);
        Height = 40;
        MinimumSize = new Size(140, 40);

        _box.BorderStyle = BorderStyle.None;
        _box.Dock = DockStyle.Fill;
        _box.BackColor = UiTheme.InputBg;
        _box.ForeColor = UiTheme.TextPrimary;
        _box.Font = new Font("Segoe UI", 10.5f);
        _box.GotFocus += (_, _) => { _focused = true; Invalidate(); };
        _box.LostFocus += (_, _) => { _focused = false; Invalidate(); };

        Controls.Add(_box);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = ClientRectangle;
        r.Width -= 1;
        r.Height -= 1;
        if (r.Width < 8 || r.Height < 8) return;

        using var path = UiTheme.CreateRoundRect(r, UiTheme.WellRadius);
        using var fill = new SolidBrush(UiTheme.InputBg);
        g.FillPath(fill, path);

        var borderColor = _focused
            ? Color.FromArgb(220, UiTheme.Accent)
            : Color.FromArgb(180, UiTheme.CardBorder);
        using var border = new Pen(borderColor, _focused ? 1.6f : 1.1f);
        g.DrawPath(border, path);

        if (_focused)
        {
            var outer = Rectangle.Inflate(r, 1, 1);
            using var glowPath = UiTheme.CreateRoundRect(outer, UiTheme.WellRadius + 1);
            using var glow = new Pen(Color.FromArgb(55, UiTheme.Accent), 2.4f);
            g.DrawPath(glow, glowPath);
        }
    }
}

internal enum SoftEmptyGlyph
{
    None,
    Dot,
    Usb,
}

/// <summary>Centered empty-state copy with quiet title + supporting line.</summary>
internal sealed class SoftEmptyState : Control
{
    private static Image? _usbPhoto;

    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public SoftEmptyGlyph Glyph { get; set; } = SoftEmptyGlyph.Dot;

    public SoftEmptyState()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
        BackColor = UiTheme.CardFace;
        Dock = DockStyle.Fill;
    }

    public void SetCopy(string title, string detail)
    {
        if (string.Equals(Title, title, StringComparison.Ordinal) &&
            string.Equals(Detail, detail, StringComparison.Ordinal))
            return;
        Title = title;
        Detail = detail;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using (var bg = new SolidBrush(Parent?.BackColor ?? BackColor))
            g.FillRectangle(bg, ClientRectangle);

        using var titleFont = UiTheme.TryFont(
            ("Segoe UI", 14f, FontStyle.Bold),
            ("Segoe UI Semibold", 13.5f, FontStyle.Bold));
        using var detailFont = new Font("Segoe UI", 10f);

        var maxW = Math.Max(40, Width - 40);
        var titleSize = TextRenderer.MeasureText(Title, titleFont, new Size(maxW, 0),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        var detailSize = TextRenderer.MeasureText(Detail, detailFont, new Size(maxW, 0),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

        var glyph = Glyph == SoftEmptyGlyph.Usb ? Math.Clamp(Math.Min(72, Height / 3), 44, 72) : 28;
        const int gapGlyph = 8;
        const int gapTitle = 6;
        var showGlyph = Glyph != SoftEmptyGlyph.None;
        var blockH = (showGlyph ? glyph + gapGlyph : 0) + titleSize.Height + gapTitle + detailSize.Height;
        var top = Math.Max(8, (Height - blockH) / 2);
        var cx = Width / 2f;

        if (Glyph == SoftEmptyGlyph.Usb)
        {
            _usbPhoto ??= UiAssets.LoadPng("usb-empty.png");
            if (_usbPhoto != null)
                DrawUsbPhoto(g, _usbPhoto, cx, top, glyph);
            else
                DrawUsbGlyph(g, cx, top, glyph, Color.FromArgb(160, UiTheme.Accent));
        }
        else if (Glyph == SoftEmptyGlyph.Dot)
        {
            using var brush = new SolidBrush(Color.FromArgb(50, UiTheme.Accent));
            g.FillEllipse(brush, cx - 5, top + 9, 10, 10);
        }

        var titleTop = showGlyph ? top + glyph + gapGlyph : top;
        var titleRect = new Rectangle(20, titleTop, maxW, titleSize.Height + 10);
        var detailRect = new Rectangle(20, titleTop + titleSize.Height + gapTitle, maxW, detailSize.Height + 14);

        TextRenderer.DrawText(g, Title, titleFont, titleRect, Color.FromArgb(248, 250, 252),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, Detail, detailFont, detailRect, Color.FromArgb(138, 155, 181),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }

    private static void DrawUsbPhoto(Graphics g, Image img, float cx, float top, float size)
    {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        // Crop the generated letterbox so the plug fills the glyph.
        var src = new Rectangle(
            (int)(img.Width * 0.26),
            (int)(img.Height * 0.10),
            Math.Max(8, (int)(img.Width * 0.48)),
            Math.Max(8, (int)(img.Height * 0.78)));
        var dest = new Rectangle((int)Math.Round(cx - size / 2f), (int)Math.Round(top), (int)size, (int)size);
        g.DrawImage(img, dest, src, GraphicsUnit.Pixel);
    }

    private static void DrawUsbGlyph(Graphics g, float cx, float top, float size, Color color)
    {
        using var pen = new Pen(color, 2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        using var fill = new SolidBrush(color);
        var stemX = cx;
        var plugTop = top + size * 0.62f;
        var plugH = size * 0.28f;
        var plugW = size * 0.42f;
        g.FillRectangle(fill, stemX - plugW / 2f, plugTop, plugW, plugH);
        g.DrawLine(pen, stemX, plugTop, stemX, top + size * 0.38f);

        g.DrawLine(pen, stemX, top + size * 0.46f, stemX - size * 0.28f, top + size * 0.28f);
        g.FillEllipse(fill, stemX - size * 0.38f, top + size * 0.18f, size * 0.18f, size * 0.18f);

        g.DrawLine(pen, stemX, top + size * 0.42f, stemX + size * 0.28f, top + size * 0.22f);
        var tx = stemX + size * 0.28f;
        var ty = top + size * 0.08f;
        g.FillPolygon(fill, new[]
        {
            new PointF(tx, ty),
            new PointF(tx + size * 0.12f, ty + size * 0.18f),
            new PointF(tx - size * 0.12f, ty + size * 0.18f),
        });

        g.DrawLine(pen, stemX, top + size * 0.38f, stemX, top + size * 0.12f);
        g.FillRectangle(fill, stemX - size * 0.08f, top, size * 0.16f, size * 0.16f);
    }
}

/// <summary>Owner-painted activity log — RichTextBox cannot participate in WS_EX_COMPOSITED.</summary>
internal sealed class SoftLogView : Control
{
    private readonly List<(string Time, string Message)> _lines = new();
    private int _scrollY;
    private const int LineHeight = 24;
    private const int MaxLines = 800;

    public SoftLogView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw, true);
        BackColor = UiTheme.InputBg;
        ForeColor = Color.FromArgb(210, 230, 250);
        Font = new Font("Consolas", 9.25f);
    }

    public void Clear()
    {
        _lines.Clear();
        _scrollY = 0;
        Invalidate();
    }

    public string GetFullText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (time, msg) in _lines)
        {
            var rawTime = time.Trim();
            var timeStr = rawTime.StartsWith("[") ? rawTime : $"[{rawTime}]";
            sb.AppendLine($"{timeStr} {msg}");
        }
        return sb.ToString();
    }

    public void Append(string time, string message)
    {
        _lines.Add((time, message));
        if (_lines.Count > MaxLines)
            _lines.RemoveRange(0, _lines.Count - MaxLines);
        ClampScroll(stickToBottom: true);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using (var bg = new SolidBrush(BackColor))
            g.FillRectangle(bg, ClientRectangle);

        if (_lines.Count == 0)
            return;

        var first = Math.Max(0, _scrollY / LineHeight);
        var last = Math.Min(_lines.Count - 1, (_scrollY + Height) / LineHeight);
        using var timeFont = new Font("Consolas", 9.25f);
        var timeColor = Color.FromArgb(100, 130, 160);
        for (var i = first; i <= last; i++)
        {
            var y = i * LineHeight - _scrollY;
            var rawTime = _lines[i].Time.Trim();
            var timeStr = rawTime.StartsWith("[") ? rawTime : $"[{rawTime}]";
            var timeW = TextRenderer.MeasureText(g, timeStr, timeFont).Width;
            var timeRect = new Rectangle(8, y, timeW + 2, LineHeight);
            var msgRect = new Rectangle(timeRect.Right + 8, y, Math.Max(20, Width - timeRect.Right - 14), LineHeight);
            const TextFormatFlags logText = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
                                            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                                            TextFormatFlags.GlyphOverhangPadding;
            TextRenderer.DrawText(g, timeStr, timeFont, timeRect, timeColor, logText);

            var msg = _lines[i].Message;
            var msgColor = (msg.Contains("device", StringComparison.OrdinalIgnoreCase) ||
                            msg.Contains("attach", StringComparison.OrdinalIgnoreCase) ||
                            msg.Contains("connect", StringComparison.OrdinalIgnoreCase) ||
                            msg.Contains("host", StringComparison.OrdinalIgnoreCase))
                ? Color.FromArgb(0, 229, 255)
                : Color.FromArgb(226, 232, 240);
            TextRenderer.DrawText(g, msg, timeFont, msgRect, msgColor, logText);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        SetScroll(_scrollY + (e.Delta > 0 ? -LineHeight * 3 : LineHeight * 3), stickToBottom: false);
        base.OnMouseWheel(e);
    }

    protected override void OnResize(EventArgs e)
    {
        ClampScroll(stickToBottom: false);
        base.OnResize(e);
    }

    private void SetScroll(int value, bool stickToBottom)
    {
        _scrollY = value;
        ClampScroll(stickToBottom);
        Invalidate();
    }

    private void ClampScroll(bool stickToBottom)
    {
        var max = Math.Max(0, _lines.Count * LineHeight - Height);
        _scrollY = stickToBottom ? max : Math.Clamp(_scrollY, 0, max);
    }
}

/// <summary>A discovered / remembered phone on the LAN.</summary>
internal sealed class OnlineServer(string host, string name)
{
    public string Host { get; } = host;
    public string Name { get; set; } = name;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public long? LastRttMs { get; set; }
    public int PingFailStreak { get; set; }
    public int? BatteryPercent { get; set; }
    public bool BatteryCharging { get; set; }
    public int? WifiQuality { get; set; }
    public string? BusyHost { get; set; }
    public string? BusyPc { get; set; }

    public string StatusChip
    {
        get
        {
            if (IsBusyElsewhere) return "In use";
            if (PingFailStreak >= 2) return "timeout";
            if (LastRttMs is long ms) return $"{ms} ms";
            return "Online";
        }
    }

    public string? BatteryChip =>
        BatteryPercent is int pct ? (BatteryCharging ? $"{pct}%+" : $"{pct}%") : null;

    public string StatusLine
    {
        get
        {
            var parts = new List<string>();
            if (WifiQuality is int wifi) parts.Add($"Wi‑Fi {wifi}%");
            if (IsBusyElsewhere)
                parts.Add(string.IsNullOrWhiteSpace(BusyPc) ? "in use on another PC" : $"in use on {BusyPc}");
            return string.Join("  ·  ", parts);
        }
    }

    public bool IsBusyElsewhere =>
        !string.IsNullOrWhiteSpace(BusyHost) && !HostIsThisPc(BusyHost);

    public void NotePing(long? rttMs)
    {
        if (rttMs is long sample)
        {
            PingFailStreak = 0;
            LastRttMs = LastRttMs is long prev
                ? (long)Math.Round(prev * 0.6 + sample * 0.4)
                : sample;
        }
        else if (LastRttMs.HasValue)
        {
            PingFailStreak++;
        }
    }

    public bool ReportsPluggedDevices { get; set; }
    public HashSet<string> PluggedBusIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> PluggedVidPids { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<RemoteUsbDevice> AdvertisedDevices { get; } = new();
    public override string ToString() => Host;

    internal static bool HostIsThisPc(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        host = host.Trim();
        if (host is "127.0.0.1" or "::1" or "localhost") return true;
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (string.Equals(ua.Address.ToString(), host, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        catch { /* ignore */ }
        return false;
    }
}

internal static class UsbClassHints
{
    public static string? VerdictChip(string? hint, string? description)
    {
        return Kind(hint, description) switch
        {
            "hid" => "Works well",
            "iso" => "Often fails",
            "stor" or "hub" or "other" => "Try it",
            _ => null,
        };
    }

    public static string Kind(string? hint, string? description)
    {
        var h = (hint ?? "").Trim().ToLowerInvariant();
        if (h is "hid" or "iso" or "stor" or "hub" or "other")
            return h;
        var d = description ?? "";
        if (LooksLike(d, "mouse", "keyboard", "hid", "gamepad", "joystick", "controller"))
            return "hid";
        if (LooksLike(d, "camera", "webcam", "audio", "headset", "microphone", "speaker"))
            return "iso";
        if (LooksLike(d, "storage", "disk", "flash", "card reader"))
            return "stor";
        if (LooksLike(d, "hub"))
            return "hub";
        return "";
    }

    private static bool LooksLike(string text, params string[] words)
    {
        foreach (var w in words)
        {
            if (text.Contains(w, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
