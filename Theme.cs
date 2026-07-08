using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace ForzaCryptoTool;

internal static class Theme
{

    public static readonly Color Bg = ColorTranslator.FromHtml("#0B0B0B");
    public static readonly Color BgSecondary = ColorTranslator.FromHtml("#121212");
    public static readonly Color Panel = ColorTranslator.FromHtml("#181818");
    public static readonly Color PanelHover = ColorTranslator.FromHtml("#1F1F1F");
    public static readonly Color Border = ColorTranslator.FromHtml("#2A2A2A");
    public static readonly Color Accent = ColorTranslator.FromHtml("#1E88FF");
    public static readonly Color AccentHover = ColorTranslator.FromHtml("#3A9BFF");
    public static readonly Color Success = ColorTranslator.FromHtml("#4CAF50");
    public static readonly Color Warning = ColorTranslator.FromHtml("#FFB300");
    public static readonly Color Error = ColorTranslator.FromHtml("#F44336");
    public static readonly Color TextMain = ColorTranslator.FromHtml("#F2F2F2");
    public static readonly Color TextMuted = ColorTranslator.FromHtml("#9AA0A6");

    public const int Sp1 = 8;
    public const int Sp2 = 16;
    public const int Sp3 = 24;
    public const int Sp4 = 32;

    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
    public static float DpiScale()
    {
        try { return GetDpiForSystem() / 96f; }
        catch { return 1f; }
    }

    public static int Scaled(int px) => (int)Math.Round(px * DpiScale());
    public static Size ScaleSize(int width, int height) => new(Scaled(width), Scaled(height));
    public static Padding ScalePadding(int all) => new(Scaled(all));
    public static Padding ScalePadding(int left, int top, int right, int bottom) =>
        new(Scaled(left), Scaled(top), Scaled(right), Scaled(bottom));
    public static RowStyle ScaleRow(int height) => new(SizeType.Absolute, Scaled(height));
    public static ColumnStyle ScaleColumn(int width) => new(SizeType.Absolute, Scaled(width));
    public static int S1 => Scaled(Sp1);
    public static int S2 => Scaled(Sp2);
    public static int S3 => Scaled(Sp3);
    public static int S4 => Scaled(Sp4);

    public static readonly Font Title = new("Segoe UI Semibold", 13f);
    public static readonly Font Section = new("Segoe UI Semibold", 10f);
    public static readonly Font Body = new("Segoe UI", 9.75f);
    public static readonly Font BodyStrong = new("Segoe UI Semibold", 9.75f);
    public static readonly Font Small = new("Segoe UI", 8.75f);
    public static readonly Font Mono = new("Consolas", 9.25f);

    public const string IconUpload = "";
    public const string IconDownload = "";
    public const string IconFolder = "";
    public const string IconFile = "";
    public const string IconSettings = "";
    public const string IconLock = "";
    public const string IconUnlock = "";
    public const string IconCheck = "";
    public const string IconError = "";
    public const string IconWarning = "";
    public const string IconCopy = "";
    public const string IconSave = "";
    public const string IconClear = "";
    public const string IconInfo = "";
    public const string IconHistory = "";
    public const string IconDatabase = "";
    public const string IconContact = "";
    public const string IconShield = "";

    public static Color Mix(Color a, Color b, float amountOfB)
    {
        float t = Math.Clamp(amountOfB, 0f, 1f);
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    public static Bitmap Glyph(string glyph, Color color, int size = 16)
    {
        int px = Scaled(size);
        var bmp = new Bitmap(px + 2, px + 2);
        bmp.SetResolution(GetDpiForSystem(), GetDpiForSystem());
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Segoe MDL2 Assets", px * 0.78f, GraphicsUnit.Pixel);
        var measured = g.MeasureString(glyph, font);
        using var brush = new SolidBrush(color);
        g.DrawString(glyph, font, brush, (bmp.Width - measured.Width) / 2f, (bmp.Height - measured.Height) / 2f);
        return bmp;
    }

    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Button MakeButton(string text, string? glyph = null, bool primary = false)
    {
        var fore = primary ? Color.White : TextMain;
        var enabledBack = primary ? Accent : PanelHover;
        var disabledBack = primary ? Mix(PanelHover, Bg, 0.35f) : Mix(PanelHover, Bg, 0.45f);
        var button = new Button
        {
            Text = text,
            Font = BodyStrong,
            Height = Scaled(40),
            AutoEllipsis = true,
            FlatStyle = FlatStyle.Flat,
            ForeColor = fore,
            BackColor = enabledBack,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            ImageAlign = ContentAlignment.MiddleLeft,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = ScalePadding(10, 0, 12, 0),
        };
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Mix(PanelHover, Accent, 0.12f);
        button.FlatAppearance.MouseDownBackColor = primary ? Mix(Accent, Color.Black, 0.2f) : Mix(PanelHover, Color.Black, 0.2f);
        if (glyph is not null)
            button.Image = Glyph(glyph, fore, 16);

        button.EnabledChanged += (_, _) =>
        {
            button.BackColor = button.Enabled ? enabledBack : disabledBack;
            button.ForeColor = button.Enabled ? fore : TextMuted;
            if (glyph is not null)
                button.Image = Glyph(glyph, button.Enabled ? fore : TextMuted, 16);
            button.Cursor = button.Enabled ? Cursors.Hand : Cursors.Default;
        };
        return button;
    }

    public static Button MakeFlowButton(string text) => new FlowButton(text);

    public static Button MakeToolButton(string glyph, string tooltip, ToolTip tips)
    {
        var button = new Button
        {
            Size = ScaleSize(32, 28),
            FlatStyle = FlatStyle.Flat,
            BackColor = Panel,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            Image = Glyph(glyph, TextMuted, 14),
            ImageAlign = ContentAlignment.MiddleCenter,
        };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.MouseOverBackColor = PanelHover;
        tips.SetToolTip(button, tooltip);
        return button;
    }

    public static Label MakeLabel(string text) => new()
    {
        Text = text,
        Font = Body,
        ForeColor = TextMuted,
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleLeft,
        Dock = DockStyle.Fill,
        Margin = new Padding(0),
    };

    public static Label MakeValue(string text) => new()
    {
        Text = text,
        Font = Body,
        ForeColor = TextMain,
        AutoSize = false,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Dock = DockStyle.Fill,
        Margin = new Padding(0),
    };

    public static Label MakeBadge() => new()
    {
        Font = new Font("Segoe UI Semibold", 8.75f),
        AutoSize = true,
        TextAlign = ContentAlignment.MiddleCenter,
        Padding = ScalePadding(Sp1, 4, Sp1, 4),
        Margin = ScalePadding(0, 4, 0, 4),
        Anchor = AnchorStyles.Left,
    };

    public static void SetBadge(Label badge, string text, Color color)
    {
        badge.Text = text;
        badge.ForeColor = color;
        badge.BackColor = Mix(Panel, color, 0.14f);
    }

    public static Label MakeStatusPill(string text, Color color) => new()
    {
        Text = text,
        Font = Small,
        ForeColor = color,
        BackColor = Mix(Panel, color, 0.12f),
        AutoSize = true,
        Padding = ScalePadding(10, 5, 10, 5),
        Margin = ScalePadding(Sp1, 0, 0, 0),
        TextAlign = ContentAlignment.MiddleCenter,
    };

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void ApplyDarkTitleBar(Form form)
    {
        int enabled = 1;
        _ = DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int));
    }
}
