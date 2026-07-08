using System.Reflection;
using System.Security.Principal;

namespace ForzaCryptoTool;

public sealed class MainForm : Form
{
    private readonly BackendClient _backend = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly System.Windows.Forms.Timer _healthTimer = new() { Interval = 60_000 };
    private readonly System.Windows.Forms.Timer _fadeTimer = new() { Interval = 15 };
    private readonly System.Windows.Forms.Timer _pageTransitionTimer = new() { Interval = 15 };

    private readonly System.Windows.Forms.Timer _ambientTimer = new() { Interval = 33 };
    private double _ambientPhase;
    private readonly ToolTip _tips = new();
    private readonly string? _initialFile;

    private DashboardView _dashboard = null!;
    private SaveSwapView _saveSwap = null!;
    private ProfileEditorView _profileEditor = null!;
    private ForzaTechView _forzaTech = null!;
    private Panel _contentHost = null!;
    private FadeOverlay _transitionOverlay = null!;

    private Label _statusDot = null!;
    private Label _statusText = null!;
    private Label _adminText = null!;
    private Button _topSettingsButton = null!;
    private Button _sidebarSettingsButton = null!;
    private bool _statusConnected;
    private Control? _activeView;
    private int _transitionTicks;
    private readonly List<(Button button, Control view)> _navItems = new();

    private sealed record NavEntry(string Label, string Glyph, Control? View, Action? Action);
    private List<NavEntry> _nav = new();
    private static Image? _logoImage;
    private static Icon? _appIcon;

    public MainForm(string? initialFile = null)
    {
        _initialFile = initialFile;

        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
        BuildUi();
        Shown += async (_, _) =>
        {
            ShowView(_dashboard);
            StartFadeIn();
            Logger.Info($"{BuildConfig.AppName} {BuildConfig.VersionLabel} ready.");
            _dashboard.LoadInitial(_initialFile);
            await RefreshHealthAsync();
            _healthTimer.Start();
            _ = CheckForUpdatesAsync();
        };
        _healthTimer.Tick += async (_, _) => await RefreshHealthAsync();
        _fadeTimer.Tick += (_, _) =>
        {
            Opacity = Math.Min(1.0, Opacity + 0.09);
            if (Opacity >= 1.0)
                _fadeTimer.Stop();
        };
        _pageTransitionTimer.Tick += (_, _) => AdvancePageTransition();
        _ambientTimer.Tick += (_, _) => AdvanceAmbient();
        _ambientTimer.Start();
        FormClosed += (_, _) => { _healthTimer.Stop(); _ambientTimer.Stop(); _backend.Dispose(); };
    }

    private float AmbientPulse => (float)((Math.Sin(_ambientPhase) + 1.0) * 0.5);

    private void AdvanceAmbient()
    {
        if (WindowState == FormWindowState.Minimized) return;
        _ambientPhase += 0.10;
        if (_ambientPhase > Math.PI * 2) _ambientPhase -= Math.PI * 2;

        foreach (var (button, view) in _navItems)
            if (ReferenceEquals(view, _activeView)) button.Invalidate(new Rectangle(0, 0, 24, button.Height));

        if (_statusConnected && _statusText is not null)
            _statusText.BackColor = Theme.Mix(Theme.Panel, Theme.Success, 0.10f + AmbientPulse * 0.10f);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyDarkTitleBar(this);
    }

    private static Image? Logo => _logoImage ??= LoadEmbeddedImage("ForzaCryptoTool.Assets.logo.png");
    private static Icon? AppIcon => _appIcon ??= LoadEmbeddedIcon("ForzaCryptoTool.Assets.app.ico");

    private static Image? LoadEmbeddedImage(string resource)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            return stream is null ? null : Image.FromStream(stream);
        }
        catch { return null; }
    }

    private static Icon? LoadEmbeddedIcon(string resource)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            return stream is null ? null : new Icon(stream);
        }
        catch { return null; }
    }

    private void BuildUi()
    {
        Text = BuildConfig.AppName;
        BackColor = Theme.Bg;
        Font = Theme.Body;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = Theme.ScaleSize(1280, 860);

        MinimumSize = Theme.ScaleSize(940, 632);
        StartPosition = FormStartPosition.CenterScreen;
        Opacity = 0d;
        if (AppIcon is not null) Icon = AppIcon;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Bg };
        root.ColumnStyles.Add(Theme.ScaleColumn(188));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var rightCol = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Bg };
        rightCol.RowStyles.Add(Theme.ScaleRow(48));
        rightCol.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(rightCol, 1, 0);

        rightCol.Controls.Add(BuildTopBar(), 0, 0);

        _contentHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Margin = new Padding(0) };
        rightCol.Controls.Add(_contentHost, 0, 1);

        _dashboard = new DashboardView(_backend, _settings);
        _saveSwap = new SaveSwapView(_backend, _settings);
        _profileEditor = new ProfileEditorView(_backend, _settings);
        _forzaTech = new ForzaTechView(_backend, _settings);
        foreach (var view in new Control[] { _dashboard, _saveSwap, _profileEditor, _forzaTech })
        {
            view.Visible = false;
            _contentHost.Controls.Add(view);
        }

        _nav = new List<NavEntry>
        {
            new(T("Nav.Dashboard"),       Theme.IconDatabase, _dashboard,     null),
            new(T("Nav.SaveSwap"),        Theme.IconContact,  _saveSwap,      null),
            new(T("Nav.ProfileEditor"),   Theme.IconUnlock,   _profileEditor, null),
            new(T("Nav.OlderForzaTech"),  Theme.IconHistory,  _forzaTech,     null),
            new(T("Nav.Settings"),        Theme.IconSettings, null,           OpenSettings),
        };

        root.Controls.Add(BuildSidebar(), 0, 0);

        _transitionOverlay = new FadeOverlay { Visible = false };
        _contentHost.Controls.Add(_transitionOverlay);
        _transitionOverlay.BringToFront();

        EnableDoubleBuffering(this);
    }

    private static readonly System.Reflection.PropertyInfo? DbProp =
        typeof(Control).GetProperty("DoubleBuffered",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    private static void EnableDoubleBuffering(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c is Panel or TableLayoutPanel or FlowLayoutPanel or ListBox or ListView)
                DbProp?.SetValue(c, true, null);
            EnableDoubleBuffering(c);
        }
    }

    private Control BuildSidebar()
    {

        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.BgSecondary,
            Margin = new Padding(0),
            ColumnCount = 1,
            RowCount = 2,
        };
        bar.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bar.Paint += (_, e) => { using var pen = new Pen(Theme.Border); e.Graphics.DrawLine(pen, bar.Width - 1, 0, bar.Width - 1, bar.Height); };

        var nav = new Panel
        {
            Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(Theme.Sp1, Theme.Sp1, Theme.Sp1, 0),
        };
        for (int i = _nav.Count - 1; i >= 0; i--)
        {
            var entry = _nav[i];
            var button = MakeNavButton(entry.Label, entry.Glyph);
            if (entry.View is not null)
            {
                var view = entry.View;
                button.Click += (_, _) => ShowView(view);
                _navItems.Add((button, view));
            }
            else if (entry.Action is not null)
            {
                var action = entry.Action;
                button.Click += (_, _) => action();
                _sidebarSettingsButton = button;
            }
            nav.Controls.Add(button);
        }
        bar.Controls.Add(nav, 0, 1);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent,
            Padding = new Padding(Theme.Sp2, 0, 0, 0), Margin = new Padding(0),
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        if (Logo is not null)
            header.Controls.Add(new PictureBox
            {
                Image = Logo, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(34, 34),
                Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, Theme.Sp1, 0), BackColor = Color.Transparent,
            }, 0, 0);
        header.Controls.Add(new Label
        {
            Text = BuildConfig.AppName, Font = Theme.BodyStrong, ForeColor = Theme.TextMain, AutoSize = true,
            Anchor = AnchorStyles.Left, BackColor = Color.Transparent, Margin = new Padding(0),
        }, 1, 0);
        bar.Controls.Add(header, 0, 0);
        return bar;
    }

    private Control BuildTopBar()
    {
        var top = new Panel { Dock = DockStyle.Fill, BackColor = Theme.BgSecondary, Margin = new Padding(0) };
        top.Paint += (_, e) => { using var pen = new Pen(Theme.Border); e.Graphics.DrawLine(pen, 0, top.Height - 1, top.Width, top.Height - 1); };

        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, AutoSize = true,
            BackColor = Color.Transparent, Padding = Theme.ScalePadding(0, 9, Theme.Sp2, 0), WrapContents = false,
        };
        _topSettingsButton = Theme.MakeButton(T("Top.Settings"), Theme.IconSettings);
        _topSettingsButton.Width = Theme.Scaled(142);
        _topSettingsButton.Height = Theme.Scaled(30);
        _topSettingsButton.Font = Theme.Small;
        _topSettingsButton.Padding = Theme.ScalePadding(12, 0, 14, 0);
        _topSettingsButton.TextAlign = ContentAlignment.MiddleCenter;
        _topSettingsButton.Margin = Theme.ScalePadding(Theme.Sp1, 0, 0, 0);
        _topSettingsButton.Click += (_, _) => OpenSettings();
        var aboutButton = Theme.MakeToolButton(Theme.IconInfo, T("Top.AboutTooltip"), _tips);
        aboutButton.Size = Theme.ScaleSize(30, 30);
        aboutButton.Margin = Theme.ScalePadding(Theme.Sp1, 0, 0, 0);
        aboutButton.Click += (_, _) => ShowAbout();
        _statusText = new Label
        {
            Text = T("Top.BackendChecking"), Font = Theme.Small, ForeColor = Theme.TextMuted, AutoSize = true,
            Margin = Theme.ScalePadding(2, 8, Theme.Sp1, 0), Cursor = Cursors.Hand, BackColor = Color.Transparent,
        };
        _statusText.Click += async (_, _) => await RefreshHealthAsync();
        _statusDot = new Label { Text = "●", Font = Theme.Small, ForeColor = Theme.TextMuted, AutoSize = true, Margin = new Padding(0, 8, 0, 0), BackColor = Color.Transparent };
        _tips.SetToolTip(_statusText, "Click to re-check backend health");
        _adminText = new Label
        {
            Text = IsElevated() ? T("Top.AdminElevated") : T("Top.AdminStandard"),
            Font = Theme.Small,
            ForeColor = IsElevated() ? Theme.Success : Theme.Warning,
            AutoSize = true,
            Margin = Theme.ScalePadding(Theme.Sp1, 8, Theme.Sp1, 0),
            BackColor = Color.Transparent,
        };
        StyleStatusPill(_statusText, T("Top.BackendChecking"), Theme.TextMuted);
        StyleStatusPill(_adminText, IsElevated() ? T("Top.AdminElevated") : T("Top.AdminStandard"), IsElevated() ? Theme.Success : Theme.Warning);
        _statusDot.Visible = false;

        right.Controls.Add(_topSettingsButton);
        right.Controls.Add(aboutButton);
        right.Controls.Add(_adminText);
        right.Controls.Add(_statusText);
        right.Controls.Add(_statusDot);
        top.Controls.Add(right);
        return top;
    }

    private Button MakeNavButton(string text, string glyph)
    {

        var button = new Button
        {
            Text = "  " + text, Font = Theme.BodyStrong, Height = Theme.Scaled(38), Dock = DockStyle.Top,
            FlatStyle = FlatStyle.Flat, ForeColor = Theme.TextMuted, BackColor = Theme.BgSecondary, Cursor = Cursors.Hand,
            TextImageRelation = TextImageRelation.ImageBeforeText, ImageAlign = ContentAlignment.MiddleLeft,
            TextAlign = ContentAlignment.MiddleLeft, Padding = Theme.ScalePadding(10, 0, Theme.Sp1, 0),
            Margin = Theme.ScalePadding(0, 0, 0, 4),
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Theme.Mix(Theme.BgSecondary, Theme.Accent, 0.10f);
        button.FlatAppearance.MouseDownBackColor = Theme.Mix(Theme.BgSecondary, Theme.Accent, 0.18f);
        button.Image = Theme.Glyph(glyph, Theme.TextMuted, 16);
        button.Paint += (_, e) =>
        {
            var active = _navItems.Any(item => ReferenceEquals(item.button, button) && ReferenceEquals(item.view, _activeView));
            if (!active) return;

            float p = AmbientPulse;
            var barColor = Theme.Mix(Theme.Accent, Theme.AccentHover, p * 0.6f);
            var glow = Color.FromArgb((int)(60 + 40 * p), Theme.Accent);
            var rect = new Rectangle(0, 6, 3, button.Height - 12);
            using (var glowBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                       new Rectangle(0, 0, 22, button.Height), glow, Color.FromArgb(0, Theme.Accent), 0f))
                e.Graphics.FillRectangle(glowBrush, new Rectangle(0, 0, 22, button.Height));
            using var brush = new SolidBrush(barColor);
            e.Graphics.FillRectangle(brush, rect);
        };
        return button;
    }

    private void ShowView(Control view)
    {
        if (ReferenceEquals(_activeView, view)) return;
        _activeView = view;
        foreach (var (button, v) in _navItems)
        {
            bool active = ReferenceEquals(v, view);
            button.ForeColor = active ? Theme.TextMain : Theme.TextMuted;
            button.BackColor = active ? Theme.Mix(Theme.BgSecondary, Theme.Accent, 0.14f) : Theme.BgSecondary;
            button.Image = Theme.Glyph(GlyphFor(v), active ? Theme.Accent : Theme.TextMuted, 16);
            v.Visible = active;
            if (active) v.BringToFront();
            button.Invalidate();
        }
        StartPageTransition();
    }

    private const int PageTransitionTicks = 14;

    private void StartPageTransition()
    {
        if (WindowState == FormWindowState.Minimized || _transitionOverlay is null) return;
        _transitionTicks = 0;
        _contentHost.Padding = new Padding(18, 0, 0, 0);
        _transitionOverlay.Alpha = 170;
        _transitionOverlay.Visible = true;
        _transitionOverlay.BringToFront();
        _pageTransitionTimer.Stop();
        _pageTransitionTimer.Start();
    }

    private void AdvancePageTransition()
    {
        _transitionTicks++;

        double t = Math.Min(1.0, (double)_transitionTicks / PageTransitionTicks);
        double eased = 1 - Math.Pow(1 - t, 3);
        _contentHost.Padding = new Padding((int)Math.Round(18 * (1 - eased)), 0, 0, 0);
        _transitionOverlay.Alpha = (int)Math.Round(170 * (1 - eased));
        if (_transitionTicks < PageTransitionTicks) return;
        _pageTransitionTimer.Stop();
        _transitionOverlay.Visible = false;
        _contentHost.Padding = new Padding(0);
    }

    private string GlyphFor(Control view) =>
        _nav.FirstOrDefault(n => ReferenceEquals(n.View, view))?.Glyph ?? Theme.IconHistory;

    private static void StyleStatusPill(Label label, string text, Color color)
    {
        label.Text = text;
        label.Font = Theme.Small;
        label.ForeColor = color;
        label.BackColor = Theme.Mix(Theme.Panel, color, 0.12f);
        label.AutoSize = true;
        label.Padding = Theme.ScalePadding(10, 5, 10, 5);
        label.Margin = Theme.ScalePadding(Theme.Sp1, 0, 0, 0);
        label.TextAlign = ContentAlignment.MiddleCenter;
    }

    private void OpenSettings()
    {
        using var dialog = new SettingsForm(_settings, _backend);
        dialog.CheckForUpdatesRequested += () => _ = CheckForUpdatesAsync(userInitiated: true);
        dialog.LanguageChanged += ApplyShellLanguage;
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _backend.Reload();
            ApplyShellLanguage();
            Logger.Detail("Settings saved.");
            _ = RefreshHealthAsync();
        }
    }

    private void ShowAbout()
    {
        using var about = new AboutForm();
        about.ShowDialog(this);
    }

    private async Task CheckForUpdatesAsync(bool userInitiated = false)
    {
        if (!userInitiated && !_settings.AutoUpdateCheck) return;
        UpdateCheckResult result;
        try { result = await UpdateService.CheckAsync(); }
        catch { return; }

        if (result.Status != UpdateCheckStatus.UpdateAvailable || result.Info is null)
        {

            if (userInitiated)
            {
                switch (result.Status)
                {
                    case UpdateCheckStatus.UpToDate:
                        MessageBox.Show(this, $"You're on the latest version ({BuildConfig.AppVersion}).",
                            "No update available", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        break;
                    case UpdateCheckStatus.NoReleases:
                        if (MessageBox.Show(this,
                            $"No published release is available to update to right now (you're on {BuildConfig.AppVersion}).\n\n" +
                            "Open the GitHub releases page to check for a manual download?",
                            "No update found", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                            UpdateService.OpenReleasesPage();
                        break;
                    default:
                        if (MessageBox.Show(this,
                            "Couldn't reach GitHub to check for updates (network error or GitHub is unavailable).\n\n" +
                            "Open the GitHub releases page in your browser instead?",
                            "Update check failed", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                            UpdateService.OpenReleasesPage();
                        break;
                }
            }
            return;
        }

        var info = result.Info;

        if (!userInitiated && string.Equals(_settings.SkippedUpdateVersion, info.Version.ToString(), StringComparison.Ordinal))
            return;

        if (IsDisposed || Disposing) return;
        PromptAndApplyUpdate(info);
    }

    private void PromptAndApplyUpdate(UpdateInfo info)
    {
        var notes = string.IsNullOrWhiteSpace(info.Notes) ? "" :
            "\n\nWhat's new:\n" + (info.Notes.Length > 600 ? info.Notes[..600] + "…" : info.Notes);
        var msg = $"A new version of {BuildConfig.AppName} is available.\n\n" +
                  $"  Installed:  {BuildConfig.AppVersion}\n" +
                  $"  Available:  {info.Version}\n" + notes +
                  "\n\nUpdate now? (The app will close, update, and reopen.)";

        var result = MessageBox.Show(this, msg, "Update available",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);

        if (result == DialogResult.No)
        {
            _settings.SkippedUpdateVersion = info.Version.ToString();
            _settings.Save();
            return;
        }
        if (result != DialogResult.Yes) return;

        _ = ApplyUpdateAsync(info);
    }

    private async Task ApplyUpdateAsync(UpdateInfo info)
    {
        using var progress = new UpdateProgressForm(info);
        progress.Show(this);
        var ok = await UpdateService.DownloadAndApplyAsync(info, progress.Progress);
        progress.Close();

        if (ok)
        {

            Application.Exit();
            return;
        }
        MessageBox.Show(this,
            "The update couldn't be downloaded automatically. Opening the releases page so you can update manually.",
            "Update failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        UpdateService.OpenReleasesPage();
    }

    private void StartFadeIn()
    {
        Opacity = 0d;
        _fadeTimer.Start();
    }

    private async Task RefreshHealthAsync()
    {
        try
        {
            var (ok, ms) = await _backend.CheckHealthAsync();
            _statusConnected = ok;
            StyleStatusPill(_statusText, ok ? T("Top.BackendConnected", ms) : T("Top.BackendOffline"), ok ? Theme.Success : Theme.Error);
        }
        catch
        {
            _statusConnected = false;
            StyleStatusPill(_statusText, T("Top.BackendOffline"), Theme.Error);
        }
    }

    private void ApplyShellLanguage()
    {
        StartReloadFade();
        Text = BuildConfig.AppName;
        _topSettingsButton.Text = T("Top.Settings");
        _sidebarSettingsButton.Text = "  " + T("Nav.Settings");
        foreach (var (button, view) in _navItems)
        {
            if (ReferenceEquals(view, _dashboard)) button.Text = "  " + T("Nav.Dashboard");
            else if (ReferenceEquals(view, _saveSwap)) button.Text = "  " + T("Nav.SaveSwap");
            else if (ReferenceEquals(view, _profileEditor)) button.Text = "  " + T("Nav.ProfileEditor");
            else if (ReferenceEquals(view, _forzaTech)) button.Text = "  " + T("Nav.OlderForzaTech");
        }
        _dashboard.ApplyLanguage();
        _saveSwap.ApplyLanguage();
        _profileEditor.ApplyLanguage();
        _forzaTech.ApplyLanguage();
        StyleStatusPill(_adminText, IsElevated() ? T("Top.AdminElevated") : T("Top.AdminStandard"), IsElevated() ? Theme.Success : Theme.Warning);
    }

    private void StartReloadFade()
    {
        _fadeTimer.Stop();
        Opacity = Math.Min(Opacity, 0.88);
        _fadeTimer.Start();
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private string T(string key, params object[] args) => Localization.T(_settings, key, args);
}
