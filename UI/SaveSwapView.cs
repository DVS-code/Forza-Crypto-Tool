namespace ForzaCryptoTool;

internal sealed class SaveSwapView : UserControl
{
    private readonly SaveSwapService _service;
    private readonly AppSettings _settings;
    private readonly ToolTip _tips = new();

    private TextBox _donorBox = null!;
    private TextBox _activeBox = null!;
    private TextBox _xuidBox = null!;
    private Button _swapButton = null!;
    private Button _resetButton = null!;
    private Button _restoreButton = null!;
    private SlimProgress _progress = null!;
    private Button _launchXboxButton = null!;
    private Button _grabButton = null!;
    private Label _grabStatus = null!;
    private Button _autoDetectButton = null!;
    private Label _autoStatus = null!;
    private ListView _steps = null!;
    private Label _resultBadge = null!;
    private Label _resultText = null!;

    public SaveSwapView(BackendClient backend, AppSettings settings)
    {
        _service = new SaveSwapService(backend);
        _settings = settings;
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        BuildUi();
    }

    public void ApplyLanguage()
    {
        Controls.Clear();
        BuildUi();
    }

    private string T(string key, params object[] args) => Localization.T(_settings, key, args);

    private void BuildUi()
    {

        BuildSwapUi();
    }

    private void BuildComingSoon()
    {
        var center = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Theme.Bg,
        };
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        center.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        center.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "Save Swap — Coming Soon",
            Font = new Font("Segoe UI Semibold", 20f),
            ForeColor = Theme.TextMain,
            AutoSize = true,
            Anchor = AnchorStyles.None,
            Margin = new Padding(Theme.Sp3, Theme.Sp3, Theme.Sp3, Theme.Sp1),
            BackColor = Color.Transparent,
        };
        var subtitle = new Label
        {
            Text = "This feature depends on FH6 profile (C_ProfileData) decryption,\n"
                 + "which is still in progress. It will unlock once ProfileData is ready.",
            Font = Theme.Body,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleCenter,
            Anchor = AnchorStyles.None,
            Margin = new Padding(Theme.Sp3, 0, Theme.Sp3, Theme.Sp3),
            BackColor = Color.Transparent,
        };

        center.Controls.Add(new Panel { BackColor = Color.Transparent }, 0, 0);
        center.Controls.Add(title, 0, 1);
        center.Controls.Add(subtitle, 0, 2);
        center.Controls.Add(new Panel { BackColor = Color.Transparent }, 0, 3);
        Controls.Add(center);
    }

    private void BuildSwapUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Bg, Padding = Theme.ScalePadding(Theme.Sp2),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        root.Controls.Add(BuildInputs(), 0, 0);
        root.Controls.Add(BuildResults(), 1, 0);
        Controls.Add(root);
    }

    private Control BuildInputs()
    {
        var col = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Bg, Margin = Theme.ScalePadding(0, 0, Theme.Sp2, 0),
        };
        col.RowStyles.Add(Theme.ScaleRow(96));
        col.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        col.RowStyles.Add(Theme.ScaleRow(58));

        var warn = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Mix(Theme.Panel, Theme.Warning, 0.14f), Margin = Theme.ScalePadding(0, 0, 0, Theme.Sp2) };
        warn.Paint += (_, e) => { using var p = new Pen(Theme.Warning); e.Graphics.DrawRectangle(p, 0, 0, warn.Width - 1, warn.Height - 1); };
        var warnLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Padding = Theme.ScalePadding(Theme.Sp2, Theme.Sp1, Theme.Sp2, Theme.Sp1),
        };
        warnLayout.ColumnStyles.Add(Theme.ScaleColumn(34));
        warnLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var warnIcon = new Label { Text = Theme.IconWarning, Font = new Font("Segoe MDL2 Assets", 16f), ForeColor = Theme.Warning, AutoSize = true, Dock = DockStyle.Top, Margin = Theme.ScalePadding(0, Theme.Sp1, Theme.Sp1, 0), BackColor = Color.Transparent };
        var warnText = new Label
        {
            Text = T("SaveSwap.Warning"),
            Font = Theme.Body, ForeColor = Theme.TextMain, BackColor = Color.Transparent,
            Dock = DockStyle.Fill, AutoEllipsis = true,
        };
        warnLayout.Controls.Add(warnIcon, 0, 0);
        warnLayout.Controls.Add(warnText, 1, 0);
        warn.Controls.Add(warnLayout);
        col.Controls.Add(warn, 0, 0);

        var card = new Card(T("SaveSwap.Title"), Theme.IconContact) { Dock = DockStyle.Fill, Margin = Theme.ScalePadding(0, 0, 0, Theme.Sp2) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 10, BackColor = Theme.Panel };
        grid.ColumnStyles.Add(Theme.ScaleColumn(112));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(Theme.ScaleColumn(112));
        grid.RowStyles.Add(Theme.ScaleRow(24));
        grid.RowStyles.Add(Theme.ScaleRow(42));
        grid.RowStyles.Add(Theme.ScaleRow(24));
        grid.RowStyles.Add(Theme.ScaleRow(42));
        grid.RowStyles.Add(Theme.ScaleRow(42));
        grid.RowStyles.Add(Theme.ScaleRow(24));
        grid.RowStyles.Add(Theme.ScaleRow(42));
        grid.RowStyles.Add(Theme.ScaleRow(50));
        grid.RowStyles.Add(Theme.ScaleRow(30));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        grid.Controls.Add(MakeCaption(T("SaveSwap.Donor")), 0, 0);
        grid.SetColumnSpan(grid.GetControlFromPosition(0, 0)!, 3);
        _donorBox = MakeTextBox();
        grid.Controls.Add(_donorBox, 0, 1);
        grid.SetColumnSpan(_donorBox, 2);
        grid.Controls.Add(MakeBrowse(T("Common.Browse"), () => BrowseInto(_donorBox)), 2, 1);

        grid.Controls.Add(MakeCaption(T("SaveSwap.Active")), 0, 2);
        grid.SetColumnSpan(grid.GetControlFromPosition(0, 2)!, 3);
        _activeBox = MakeTextBox();
        grid.Controls.Add(_activeBox, 0, 3);
        grid.SetColumnSpan(_activeBox, 2);
        grid.Controls.Add(MakeBrowse(T("Common.Browse"), () => BrowseInto(_activeBox)), 2, 3);

        var autoRow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Margin = Theme.ScalePadding(0, 2, 0, 0), WrapContents = true };
        _autoDetectButton = Theme.MakeButton(T("SaveSwap.AutoDetect"), Theme.IconHistory);
        _autoDetectButton.Width = Theme.Scaled(240); _autoDetectButton.Height = Theme.Scaled(34);
        _autoDetectButton.Click += async (_, _) => await AutoDetectActiveSave();
        _autoStatus = new Label { AutoSize = true, Font = Theme.Small, ForeColor = Theme.TextMuted, Margin = Theme.ScalePadding(Theme.Sp1, 7, 0, 0) };
        autoRow.Controls.Add(_autoDetectButton);
        autoRow.Controls.Add(_autoStatus);
        grid.Controls.Add(autoRow, 0, 4);
        grid.SetColumnSpan(autoRow, 3);

        grid.Controls.Add(MakeCaption(T("SaveSwap.TargetXuid")), 0, 5);
        grid.SetColumnSpan(grid.GetControlFromPosition(0, 5)!, 3);
        _xuidBox = MakeTextBox();
        grid.Controls.Add(_xuidBox, 0, 6);
        grid.SetColumnSpan(_xuidBox, 2);

        var xuidButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Margin = Theme.ScalePadding(0, 4, 0, 0), WrapContents = true };
        _launchXboxButton = Theme.MakeButton(T("Common.LaunchXbox"), Theme.IconContact);
        _launchXboxButton.Width = Theme.Scaled(190); _launchXboxButton.Height = Theme.Scaled(38);
        _launchXboxButton.Click += (_, _) => { Logger.Info("Launching Xbox App."); XuidGrabber.LaunchXboxApp(); };
        _grabButton = Theme.MakeButton(T("Common.GrabXuid"), Theme.IconShield, primary: true);
        _grabButton.Width = Theme.Scaled(160); _grabButton.Height = Theme.Scaled(38); _grabButton.Margin = Theme.ScalePadding(Theme.Sp1, 0, 0, 0);
        _grabButton.Click += async (_, _) => await GrabXuidAsync();
        xuidButtons.Controls.Add(_launchXboxButton);
        xuidButtons.Controls.Add(_grabButton);
        grid.Controls.Add(xuidButtons, 0, 7);
        grid.SetColumnSpan(xuidButtons, 3);

        _grabStatus = new Label
        {
            Dock = DockStyle.Fill, Font = Theme.Small, ForeColor = Theme.TextMuted, TextAlign = ContentAlignment.MiddleLeft,
            Text = T("SaveSwap.GrabHint"),
        };
        grid.Controls.Add(_grabStatus, 0, 8);
        grid.SetColumnSpan(_grabStatus, 3);

        card.Controls.Add(grid);
        col.Controls.Add(card, 0, 1);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _swapButton = Theme.MakeButton(T("SaveSwap.SwapSave"), Theme.IconShield, primary: true);
        _swapButton.Dock = DockStyle.Fill;
        _swapButton.Margin = Theme.ScalePadding(0, Theme.Sp1, Theme.Sp1, Theme.Sp1);
        _swapButton.Click += async (_, _) => await RunSwapAsync();
        _resetButton = Theme.MakeButton(T("Common.Reset"), Theme.IconClear);
        var resetButton = _resetButton;
        resetButton.Dock = DockStyle.Fill;
        resetButton.Margin = Theme.ScalePadding(0, Theme.Sp1, Theme.Sp1, Theme.Sp1);
        resetButton.Click += (_, _) => { _donorBox.Clear(); _activeBox.Clear(); _xuidBox.Clear(); };

        _restoreButton = Theme.MakeButton(T("SaveSwap.Restore"), Theme.IconHistory);
        _restoreButton.Dock = DockStyle.Fill;
        _restoreButton.Margin = Theme.ScalePadding(0, Theme.Sp1, 0, Theme.Sp1);
        _restoreButton.Click += async (_, _) => await RestoreOriginalAsync();

        actions.Controls.Add(_swapButton, 0, 0);
        actions.Controls.Add(resetButton, 1, 0);
        actions.Controls.Add(_restoreButton, 2, 0);
        col.Controls.Add(actions, 0, 2);

        _activeBox.TextChanged += (_, _) => UpdateRestoreEnabled();
        UpdateRestoreEnabled();
        return col;
    }

    private void UpdateRestoreEnabled()
    {
        var active = _activeBox.Text.Trim();
        _restoreButton.Enabled = !string.IsNullOrWhiteSpace(active) && File.Exists(active) && _service.HasBackup(active);
        _restoreButton.Text = _restoreButton.Enabled ? T("SaveSwap.Restore") : T("SaveSwap.RestoreNoBackup");
    }

    private async Task RestoreOriginalAsync()
    {
        var active = _activeBox.Text.Trim();
        if (!File.Exists(active)) { Warn("Select the active save first."); return; }
        if (!_service.HasBackup(active)) { Warn("No pre-swap backup found for this save (the tool creates one each swap)."); return; }

        var confirm = MessageBox.Show(
            $"Restore the ORIGINAL save from the tool's pre-swap backup?\n\n{active}\n\n"
            + "This overwrites the current (swapped) save with the backup the tool made before the last swap.\n"
            + "CLOSE Forza Horizon 6 first so it doesn't hold the files open.\n\nContinue?",
            "Restore original save", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1);
        if (confirm != DialogResult.Yes) { Logger.Info("Restore cancelled."); return; }

        _swapButton.Enabled = false; _resetButton.Enabled = false; _restoreButton.Enabled = false;
        Theme.SetBadge(_resultBadge, "Restoring…", Theme.Accent);
        _resultText.ForeColor = Theme.TextMain;
        _resultText.Text = "Restoring the original save from the pre-swap backup…";
        _steps.Items.Clear();
        try
        {
            var result = await Task.Run(() => _service.RestoreOriginal(active));
            RenderResult(result);
        }
        catch (Exception ex)
        {
            Logger.Exception("Restore failed", ex);
            Theme.SetBadge(_resultBadge, "Failed", Theme.Error);
            _resultText.ForeColor = Theme.Error; _resultText.Text = ex.Message;
        }
        finally
        {
            _swapButton.Enabled = true; _resetButton.Enabled = true; UpdateRestoreEnabled();
        }
    }

    private Control BuildResults()
    {
        var card = new Card(T("SaveSwap.Result"), Theme.IconCheck) { Dock = DockStyle.Fill, Margin = new Padding(0) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Panel };
        layout.RowStyles.Add(Theme.ScaleRow(38));
        layout.RowStyles.Add(Theme.ScaleRow(60));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _resultBadge = Theme.MakeBadge();
        Theme.SetBadge(_resultBadge, T("Common.Idle"), Theme.TextMuted);
        layout.Controls.Add(_resultBadge, 0, 0);

        _resultText = new Label { Dock = DockStyle.Top, Height = 36, Font = Theme.Body, ForeColor = Theme.TextMuted, Text = T("SaveSwap.FillHint") };
        _progress = new SlimProgress { Dock = DockStyle.Bottom, Visible = false, Margin = Theme.ScalePadding(0, 4, 0, 0) };
        var midPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel };
        midPanel.Controls.Add(_resultText);
        midPanel.Controls.Add(_progress);
        layout.Controls.Add(midPanel, 0, 1);

        _steps = new DarkListView
        {
            Dock = DockStyle.Fill, View = View.Details, BackColor = Theme.Bg, ForeColor = Theme.TextMain, BorderStyle = BorderStyle.None,
            FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, Font = Theme.Body,
        };
        _steps.Columns.Add(T("SaveSwap.Step"), Theme.Scaled(150));
        _steps.Columns.Add("", Theme.Scaled(28));
        _steps.Columns.Add(T("SaveSwap.Detail"), Theme.Scaled(220));
        _steps.Resize += (_, _) => ResizeStepColumns();
        ResizeStepColumns();
        layout.Controls.Add(_steps, 0, 2);
        card.Controls.Add(layout);
        return card;
    }

    private void ResizeStepColumns()
    {
        if (_steps.Columns.Count < 3) return;
        int ok = Theme.Scaled(28);
        int step = Math.Max(Theme.Scaled(120), _steps.ClientSize.Width / 3);
        int detail = Math.Max(Theme.Scaled(160), _steps.ClientSize.Width - step - ok - Theme.Scaled(6));
        _steps.Columns[0].Width = step;
        _steps.Columns[1].Width = ok;
        _steps.Columns[2].Width = detail;
    }

    private async Task RunSwapAsync()
    {
        var donor = _donorBox.Text.Trim();
        var active = _activeBox.Text.Trim();
        if (!File.Exists(donor)) { Warn("Select a donor save."); return; }
        if (!File.Exists(active)) { Warn("Select the active save (the file the game loads — it will be overwritten). Its version is read so the swap matches your game build."); return; }
        if (string.IsNullOrWhiteSpace(_xuidBox.Text)) { Warn("Enter the target XUID."); return; }

        Logger.Info("Donor mode — re-encrypts under the donor's own T0/derived IVs; the donor version is set to your active save's so the game accepts it (works on any build, any donor size).");

        var confirm = MessageBox.Show(
            $"This will OVERWRITE the active save:\n\n{active}\n\nA timestamped backup will be created first.\n\n"
            + "After it completes: CLOSE Forza Horizon 6 before launching, so it does not autosave over the new file.\n\nContinue?",
            "Confirm destructive operation", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) { Logger.Info("Save swap cancelled by user."); return; }

        _swapButton.Enabled = false;
        _resetButton.Enabled = false;
        Theme.SetBadge(_resultBadge, "Working…", Theme.Accent);
        _resultText.ForeColor = Theme.TextMain;
        _resultText.Text = "Swapping… decrypting donor, patching XUID, re-encrypting and writing the save. This can take a few seconds — please wait.";
        _steps.Items.Clear();
        Logger.Info("Save swap started (IV-based).");

        using var anim = new System.Windows.Forms.Timer { Interval = 60 };
        int dir = 1; int v = 0;
        _progress.Value = 0; _progress.Visible = true;
        anim.Tick += (_, _) => { v += dir * 4; if (v >= 100) { v = 100; dir = -1; } else if (v <= 0) { v = 0; dir = 1; } _progress.Value = v; };
        anim.Start();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {

            string xuid = _xuidBox.Text;

            var result = await Task.Run(() => _service.SwapWithIvsAsync(donor, "", active, null, xuid));
            Logger.Info($"Save swap finished in {sw.Elapsed.TotalSeconds:0.0}s.");
            RenderResult(result);
        }
        catch (Exception ex)
        {
            Logger.Exception("Save swap failed", ex);
            Theme.SetBadge(_resultBadge, "Failed", Theme.Error);
            _resultText.ForeColor = Theme.Error;
            _resultText.Text = ex.Message;
        }
        finally
        {
            anim.Stop();
            _progress.Visible = false;
            _swapButton.Enabled = true;
            _resetButton.Enabled = true;
            UpdateRestoreEnabled();
        }
    }

    private async Task AutoDetectActiveSave()
    {
        _autoDetectButton.Enabled = false;
        _autoStatus.ForeColor = Theme.Warning;
        _autoStatus.Text = "Scanning drives…";
        Logger.Info("Auto-detecting FH6 profile save (PGS gameplay plus fallback stores).");
        try
        {
            var candidates = await Task.Run(SaveLocator.FindCandidates);
            if (candidates.Count == 0)
            {
                _autoStatus.ForeColor = Theme.Error;
                _autoStatus.Text = "No FH6 profile save found. Use Browse.";
                Logger.Warn("Auto-detect found no FH6 profile save.");
                return;
            }

            Logger.Success($"Auto-detect found {candidates.Count} profile save(s).");
            foreach (var c in candidates)
                Logger.Detail($"  {FormatCandidate(c)}");

            SaveLocator.Candidate chosen;
            if (candidates.Count == 1)
            {
                chosen = candidates[0];
            }
            else
            {
                var picked = PickCandidate(candidates);
                if (picked is null)
                {
                    _autoStatus.ForeColor = Theme.Warning;
                    _autoStatus.Text = $"{candidates.Count} saves found — none selected.";
                    return;
                }
                chosen = picked;
            }

            _activeBox.Text = chosen.Path;
            _autoStatus.ForeColor = Theme.Success;
            _autoStatus.Text = $"{chosen.Source} • {chosen.Size / 1024.0:0} KB • {candidates.Count} found";
            Logger.Success($"Selected active save: {FormatCandidate(chosen)}");
        }
        catch (Exception ex)
        {
            _autoStatus.ForeColor = Theme.Error;
            _autoStatus.Text = ex.Message;
            Logger.Exception("Auto-detect failed", ex);
        }
        finally
        {
            _autoDetectButton.Enabled = true;
        }
    }

    private static string FormatCandidate(SaveLocator.Candidate c, bool recommended = false)
    {
        var user = Path.GetFileName(Path.GetDirectoryName(c.Path) ?? "");
        var prefix = string.IsNullOrEmpty(user) ? "" : $"{user}\\";

        var tag = (recommended || c.Recommended) ? "RECOMMENDED  -  " : "";
        return $"{tag}{prefix}{Path.GetFileName(c.Path)}  -  {c.Source}  -  {c.ProfileKind}  -  {c.Size / 1024.0:0} KB  -  {c.Modified:yyyy-MM-dd HH:mm}";
    }

    private SaveLocator.Candidate? PickCandidate(IReadOnlyList<SaveLocator.Candidate> candidates)
    {
        using var dialog = new Form
        {
            Text = "Select profile save",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.Sizable,
            MinimizeBox = false, MaximizeBox = true, ShowIcon = false,
            BackColor = Theme.Bg, ForeColor = Theme.TextMain, Font = Theme.Body,
            ClientSize = Theme.ScaleSize(920, 380), MinimumSize = Theme.ScaleSize(560, 280), Padding = Theme.ScalePadding(Theme.Sp2),
        };

        var heading = new Label
        {
            Dock = DockStyle.Top, Height = Theme.Scaled(28), ForeColor = Theme.TextMuted, Font = Theme.Small,
            Text = $"{candidates.Count} FH6 profile saves found. Pick the one to use as the active save:",
        };
        var list = new ListBox
        {
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Panel,
            ForeColor = Theme.TextMain, Font = Theme.Body, IntegralHeight = false, ItemHeight = Theme.Scaled(22),
            HorizontalScrollbar = true,
        };

        for (int i = 0; i < candidates.Count; i++)
            list.Items.Add(FormatCandidate(candidates[i], recommended: i == 0));
        list.SelectedIndex = 0;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = Theme.Scaled(52), FlowDirection = FlowDirection.RightToLeft, BackColor = Theme.Bg,
        };
        var ok = Theme.MakeButton("Use selected", Theme.IconCheck, primary: true);
        ok.Width = Theme.Scaled(160); ok.DialogResult = DialogResult.OK;
        var cancel = Theme.MakeButton("Cancel", Theme.IconClear);
        cancel.Width = Theme.Scaled(116); cancel.DialogResult = DialogResult.Cancel; cancel.Margin = Theme.ScalePadding(Theme.Sp1, 0, 0, 0);
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        list.DoubleClick += (_, _) => { if (list.SelectedIndex >= 0) dialog.DialogResult = DialogResult.OK; };

        dialog.Controls.Add(list);
        dialog.Controls.Add(buttons);
        dialog.Controls.Add(heading);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;

        return dialog.ShowDialog(this) == DialogResult.OK && list.SelectedIndex >= 0
            ? candidates[list.SelectedIndex]
            : null;
    }

    private async Task GrabXuidAsync()
    {
        _grabButton.Enabled = false;
        _launchXboxButton.Enabled = false;
        SetGrabStatus("Looking for the Xbox App…", Theme.Warning);
        Logger.Info("Grab XUID started.");
        try
        {
            void Status(string s) => BeginInvoke(() => SetGrabStatus(s, Theme.Warning));
            var xuid = await Task.Run(() => XuidGrabber.GrabAsync(Status));
            _xuidBox.Text = $"0x{xuid:X16}";
            SetGrabStatus($"XUID grabbed: 0x{xuid:X16}", Theme.Success);
        }
        catch (Exception ex)
        {
            SetGrabStatus(ex.Message, Theme.Error);
            Logger.Warn($"Grab XUID failed: {ex.Message}");
            if (ex.Message.Contains("Administrator", StringComparison.OrdinalIgnoreCase))
            {
                var choice = MessageBox.Show(
                    ex.Message + "\n\nRelaunch ForzaCryptoTool as Administrator now?",
                    "Grab XUID", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (choice == DialogResult.Yes)
                {
                    if (XuidGrabber.RelaunchAsAdmin())
                        Application.Exit();
                    else
                        SetGrabStatus("Elevation was cancelled.", Theme.Warning);
                }
            }
        }
        finally
        {
            _grabButton.Enabled = true;
            _launchXboxButton.Enabled = true;
        }
    }

    private void SetGrabStatus(string text, Color color)
    {
        _grabStatus.Text = text;
        _grabStatus.ForeColor = color;
    }

    private void RenderResult(SaveSwapService.SwapResult result)
    {
        _steps.Items.Clear();
        foreach (var step in result.Steps)
        {
            var item = new ListViewItem(step.Name);
            item.SubItems.Add(step.Ok ? "✓" : "✕");
            item.SubItems.Add(step.Detail);
            item.ForeColor = step.Ok ? Theme.TextMain : Theme.Error;
            _steps.Items.Add(item);
        }

        if (result.Success)
        {
            Theme.SetBadge(_resultBadge, "Completed", Theme.Success);
            _resultText.ForeColor = Theme.Success;
            Logger.Success("Save swap completed.");
        }
        else if (result.Blocked)
        {
            Theme.SetBadge(_resultBadge, "Blocked (safe)", Theme.Warning);
            _resultText.ForeColor = Theme.Warning;
            Logger.Warn("Save swap blocked before any write.");
        }
        else
        {
            Theme.SetBadge(_resultBadge, "Failed", Theme.Error);
            _resultText.ForeColor = Theme.Error;
            Logger.Error("Save swap failed.");
        }
        _resultText.Text = result.Message;
    }

    private void Warn(string message)
    {
        Logger.Warn(message);
        MessageBox.Show(message, "Save Swap", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static Label MakeCaption(string text) => new()
    {
        Text = text, Font = Theme.Small, ForeColor = Theme.TextMuted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft,
    };

    private static TextBox MakeTextBox() => new()
    {
        Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.BgSecondary, ForeColor = Theme.TextMain,
        Font = Theme.Body, Margin = Theme.ScalePadding(0, 4, Theme.Sp1, 4),
    };

    private static Button MakeBrowse(string text, Action onClick)
    {

        var b = Theme.MakeButton(text);
        b.TextAlign = ContentAlignment.MiddleCenter;
        b.Padding = new Padding(0);
        b.Dock = DockStyle.Fill;
        b.Margin = Theme.ScalePadding(0, 4, 0, 4);
        b.Click += (_, _) => onClick();
        return b;
    }

    private void BrowseInto(TextBox target, string filter = "Profile saves (*.*)|*.*")
    {
        using var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.FileName;
    }
}
