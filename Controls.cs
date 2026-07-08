using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ForzaCryptoTool;

internal sealed class FlowButton : Button
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 30 };
    private float _phase;
    private bool _hover;

    public FlowButton(string text)
    {
        Text = text;
        Font = Theme.BodyStrong;
        Height = Theme.Scaled(40);
        FlatStyle = FlatStyle.Flat;
        ForeColor = Color.White;
        BackColor = Theme.Accent;
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
        FlatAppearance.BorderSize = 0;
        TextAlign = ContentAlignment.MiddleCenter;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        _timer.Tick += (_, _) => { _phase += 0.035f; if (_phase > 1.4f) _phase -= 1.4f; Invalidate(); };
        MouseEnter += (_, _) => { if (Enabled) { _hover = true; _timer.Start(); } };
        MouseLeave += (_, _) => { _hover = false; _timer.Stop(); Invalidate(); };
        EnabledChanged += (_, _) =>
        {
            if (!Enabled) { _hover = false; _timer.Stop(); }
            ForeColor = Enabled ? Color.White : Theme.TextMuted;
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var baseColor = Enabled ? Theme.Accent : Theme.Mix(Theme.PanelHover, Theme.Bg, 0.35f);
        using (var fill = new SolidBrush(baseColor)) g.FillRectangle(fill, ClientRectangle);

        if (_hover && Enabled)
        {
            int w = Width, bandW = Math.Max(70, w / 3);
            float x = _phase * (w + bandW) - bandW;
            using var path = new GraphicsPath();
            path.AddPolygon(new[]
            {
                new PointF(x, 0), new PointF(x + bandW, 0),
                new PointF(x + bandW - 18, Height), new PointF(x - 18, Height),
            });
            using var sheen = new LinearGradientBrush(
                new RectangleF(x - 18, 0, bandW + 36, Height), Color.White, Color.White, LinearGradientMode.Horizontal)
            {
                InterpolationColors = new ColorBlend(3)
                {
                    Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(64, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) },
                    Positions = new[] { 0f, 0.5f, 1f },
                },
            };
            var saved = g.Clip; g.SetClip(ClientRectangle);
            g.FillPath(sheen, path);
            g.Clip = saved;
        }

        TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class Card : Panel
{
    private readonly string _title;
    private readonly string _glyph;
    private readonly List<Control> _headerControls = new();

    public Card(string title, string glyph)
    {
        _title = title;
        _glyph = glyph;
        BackColor = Theme.Panel;
        Padding = Theme.ScalePadding(Theme.Sp2, 46, Theme.Sp2, Theme.Sp2);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    public void AddHeaderControl(Control control)
    {
        _headerControls.Add(control);
        Controls.Add(control);
        control.BringToFront();
        PositionHeaderControls();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        PositionHeaderControls();
    }

    private void PositionHeaderControls()
    {
        int x = Width - Theme.S2;
        foreach (var control in _headerControls)
        {
            x -= control.Width;
            control.Location = new Point(x, (Theme.Scaled(40) - control.Height) / 2 + Theme.Scaled(1));
            x -= Theme.S1;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Theme.RoundedRect(rect, 7))
        using (var fill = new SolidBrush(Theme.Panel))
        using (var border = new Pen(Theme.Border))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

        var iconRect = new Rectangle(Theme.S2, Theme.Scaled(9), Theme.Scaled(18), Theme.Scaled(22));
        using (var headerIconFont = new Font("Segoe MDL2 Assets", 12f, GraphicsUnit.Point))
            TextRenderer.DrawText(g, _glyph, headerIconFont, iconRect,
                Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        var headerRight = _headerControls.Count == 0
            ? Width - Theme.S2
            : _headerControls.Min(c => c.Left) - Theme.S1;
        var titleRect = new Rectangle(
            Theme.S2 + Theme.Scaled(24),
            Theme.Scaled(7),
            Math.Max(Theme.Scaled(80), headerRight - (Theme.S2 + Theme.Scaled(24))),
            Theme.Scaled(26));
        TextRenderer.DrawText(g, _title, Theme.Section, titleRect, Theme.TextMain,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        using (var line = new Pen(Theme.Border))
            g.DrawLine(line, 1, Theme.Scaled(39), Width - 2, Theme.Scaled(39));
    }
}

internal sealed class AccentCheckBox : CheckBox
{
    public AccentCheckBox()
    {
        AutoSize = false;
        FlatStyle = FlatStyle.Flat;
        BackColor = Color.Transparent;
        ForeColor = Theme.TextMain;
        Font = Theme.Body;
        Cursor = Cursors.Hand;
        Height = Theme.Scaled(26);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Panel);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        const int box = 16;
        var y = (Height - box) / 2;
        var rect = new Rectangle(0, y, box, box);
        using (var path = Theme.RoundedRect(rect, 4))
        {
            if (Checked)
            {
                using var fill = new SolidBrush(Enabled ? Theme.Accent : Theme.Mix(Theme.Accent, Theme.Bg, 0.55f));
                g.FillPath(fill, path);
                using var tick = new Pen(Color.White, 2f);
                g.DrawLines(tick, new[] { new Point(4, y + 8), new Point(7, y + 11), new Point(12, y + 5) });
            }
            else
            {
                using var fill = new SolidBrush(Theme.Panel);
                using var border = new Pen(Enabled ? Theme.Border : Theme.Mix(Theme.Border, Theme.Bg, 0.5f), 1.5f);
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }
        }

        var textRect = new Rectangle(box + Theme.S1, 0, Width - box - Theme.S1, Height);
        TextRenderer.DrawText(
            g,
            Text,
            Font,
            textRect,
            Enabled ? ForeColor : Theme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class DropZone : Panel
{
    public event Action<string[]>? FilesDropped;
    public event Action? BrowseRequested;
    public string PrimaryText { get; set; } = "Drop file here";
    public string SecondaryText { get; set; } = "or click to browse  -  GameDB (.slt)  -  Profile Data  -  auto-detected";

    private bool _dragActive;
    private bool _mouseOver;
    private float _dashOffset;
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 50 };

    private readonly System.Windows.Forms.Timer _idleBreath = new() { Interval = 40 };
    private double _breathPhase;
    private float Breath => (float)((Math.Sin(_breathPhase) + 1.0) * 0.5);

    public DropZone()
    {
        AllowDrop = true;
        BackColor = Theme.Panel;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        _animation.Tick += (_, _) => { _dashOffset -= 1.2f; Invalidate(); };

        _idleBreath.Tick += (_, _) =>
        {
            _breathPhase += 0.07; if (_breathPhase > Math.PI * 2) _breathPhase -= Math.PI * 2;
            if (!_dragActive) { _dashOffset -= 0.8f; Invalidate(); }
        };
        _idleBreath.Start();
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            e.Effect = DragDropEffects.Copy;
            _dragActive = true;
            _animation.Start();
            Invalidate();
        }
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        _dragActive = false;
        _animation.Stop();
        Invalidate();
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        _dragActive = false;
        _animation.Stop();
        Invalidate();
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            FilesDropped?.Invoke(files);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _mouseOver = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _mouseOver = false; Invalidate(); }
    protected override void OnClick(EventArgs e) { base.OnClick(e); BrowseRequested?.Invoke(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var idleBorder = Theme.Mix(Theme.Accent, Theme.AccentHover, Breath);
        var borderColor = _dragActive ? Theme.Accent : _mouseOver ? Theme.AccentHover : idleBorder;
        var rect = new Rectangle(2, 2, Width - 5, Height - 5);
        using (var path = Theme.RoundedRect(rect, 8))
        using (var pen = new Pen(borderColor, 1.6f) { DashStyle = DashStyle.Dash, DashOffset = _dashOffset })
            g.DrawPath(pen, path);

        var iconColor = _dragActive || _mouseOver ? Theme.Accent : Theme.Mix(Theme.TextMuted, Theme.Accent, Breath * 0.55f);
        using (var iconFont = new Font("Segoe MDL2 Assets", 22f, GraphicsUnit.Point))
        {
            var icon = Theme.IconUpload;
            var size = g.MeasureString(icon, iconFont);
            using var brush = new SolidBrush(iconColor);
            g.DrawString(icon, iconFont, brush, (Width - size.Width) / 2f, Height / 2f - 44);
        }

        using var primaryFont = new Font("Segoe UI Semibold", 11.5f);
        DrawCentered(g, PrimaryText, primaryFont, Theme.TextMain, Height / 2f + 2);
        DrawCentered(g, SecondaryText, Theme.Small, Theme.TextMuted, Height / 2f + 26);
    }

    private void DrawCentered(Graphics g, string text, Font font, Color color, float y)
    {
        var rect = new Rectangle(Theme.S2, (int)y - Theme.Scaled(2), Math.Max(1, Width - Theme.S4), Theme.Scaled(24));
        TextRenderer.DrawText(g, text, font, rect, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class DarkListView : ListView
{
    private int _hotIndex = -1;

    public DarkListView()
    {
        OwnerDraw = true;
        View = View.Details;
        FullRowSelect = true;
        HideSelection = false;
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Panel;
        ForeColor = Theme.TextMain;
        Font = Theme.Body;
        GridLines = false;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitTest(e.Location);
        int index = hit.Item?.Index ?? -1;
        if (index == _hotIndex) return;
        _hotIndex = index;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hotIndex = -1;
        Invalidate();
    }

    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        using var back = new SolidBrush(Theme.BgSecondary);
        e.Graphics.FillRectangle(back, e.Bounds);
        using var line = new Pen(Theme.Border);
        e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", Theme.Small, new Rectangle(e.Bounds.Left + 8, e.Bounds.Top, e.Bounds.Width - 12, e.Bounds.Height),
            Theme.TextMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    protected override void OnDrawItem(DrawListViewItemEventArgs e)
    {
        if (View != View.Details)
        {
            e.DrawDefault = true;
            return;
        }
    }

    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        if (e.Item is null || e.SubItem is null) return;
        var selected = e.Item.Selected;
        var hot = e.ItemIndex == _hotIndex;
        var backColor = selected ? Theme.Mix(Theme.Panel, Theme.Accent, 0.26f) :
            hot ? Theme.PanelHover : Theme.Panel;
        using (var back = new SolidBrush(backColor))
            e.Graphics.FillRectangle(back, e.Bounds);
        using (var line = new Pen(Theme.Mix(Theme.Border, Theme.Bg, 0.35f)))
            e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

        var color = e.Item.ForeColor == Color.Empty ? Theme.TextMain : e.Item.ForeColor;
        if (e.ColumnIndex > 0 && !selected) color = Theme.TextMuted;
        TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Font, new Rectangle(e.Bounds.Left + 8, e.Bounds.Top, e.Bounds.Width - 12, e.Bounds.Height),
            selected ? Theme.TextMain : color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class FadeOverlay : Panel
{
    private int _alpha;

    public FadeOverlay()
    {
        Dock = DockStyle.Fill;
        Enabled = false;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    public int Alpha
    {
        get => _alpha;
        set { _alpha = Math.Clamp(value, 0, 180); Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_alpha <= 0) return;
        using var brush = new SolidBrush(Color.FromArgb(_alpha, Theme.Bg));
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }
}

internal sealed class SlimProgress : Control
{
    private int _value;

    public SlimProgress()
    {
        Height = 8;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    public int Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 100); Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var track = new Rectangle(0, Height / 2 - 3, Width - 1, 6);
        using (var path = Theme.RoundedRect(track, 3))
        using (var brush = new SolidBrush(Theme.PanelHover))
            g.FillPath(brush, path);

        int fillWidth = (int)((Width - 1) * (_value / 100f));
        if (fillWidth > 6)
        {
            var fill = new Rectangle(0, Height / 2 - 3, fillWidth, 6);
            using var path = Theme.RoundedRect(fill, 3);
            using var brush = new SolidBrush(Theme.Accent);
            g.FillPath(brush, path);
        }
    }
}

internal sealed class DarkComboBox : ComboBox
{
    public DarkComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        BackColor = Theme.BgSecondary;
        ForeColor = Theme.TextMain;
        Font = Theme.Body;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) { base.OnDrawItem(e); return; }
        bool selected = (e.State & DrawItemState.Selected) != 0;
        var back = selected ? Theme.Mix(Theme.BgSecondary, Theme.Accent, 0.30f) : Theme.BgSecondary;
        using (var b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);
        TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Theme.Body, e.Bounds,
            Theme.TextMain, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (m.Msg == 0xF || m.Msg == 0x85)
        {
            using var g = CreateGraphics();
            int aw = 18;
            var well = new Rectangle(Width - aw - 1, 1, aw, Height - 2);
            using (var b = new SolidBrush(Theme.BgSecondary)) g.FillRectangle(b, well);

            int cx = Width - aw / 2 - 2, cy = Height / 2;
            using var pen = new Pen(Theme.TextMain, 1.6f);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.DrawLines(pen, new[] { new Point(cx - 4, cy - 2), new Point(cx, cy + 2), new Point(cx + 4, cy - 2) });
            using var border = new Pen(Theme.Border);
            g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        }
    }
}
