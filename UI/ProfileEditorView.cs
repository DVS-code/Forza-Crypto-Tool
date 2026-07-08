namespace ForzaCryptoTool;

internal sealed class ProfileEditorView : UserControl
{
    private readonly BackendClient _backend;
    private readonly AppSettings _settings;

    private TextBox _profileBox = null!;
    private TextBox _xuidBox = null!;
    private Button _decryptButton = null!;
    private Button _saveButton = null!;
    private Button _grabButton = null!;
    private Label _status = null!;
    private Label _detected = null!;
    private ListView _fields = null!;

    private byte[]? _decryptedPlaintext;
    private ulong? _currentXuid;
    private int _pendingEdits;

    public ProfileEditorView(BackendClient backend, AppSettings settings)
    {
        _backend = backend;
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
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Bg, Padding = Theme.ScalePadding(Theme.Sp3),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.Controls.Add(BuildInputs(), 0, 0);
        root.Controls.Add(BuildPreview(), 1, 0);
        Controls.Add(root);
    }

    private Control BuildInputs()
    {
        var col = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Bg, Margin = Theme.ScalePadding(0, 0, Theme.Sp2, 0),
        };
        col.RowStyles.Add(Theme.ScaleRow(118));
        col.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        col.RowStyles.Add(Theme.ScaleRow(58));

        var info = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Mix(Theme.Panel, Theme.Accent, 0.12f), Margin = Theme.ScalePadding(0, 0, 0, Theme.Sp2), Padding = Theme.ScalePadding(Theme.Sp2, Theme.Sp1, Theme.Sp2, Theme.Sp1) };
        info.Paint += (_, e) => { using var p = new Pen(Theme.Accent); e.Graphics.DrawRectangle(p, 0, 0, info.Width - 1, info.Height - 1); };
        var infoLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent };
        infoLayout.ColumnStyles.Add(Theme.ScaleColumn(34));
        infoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var infoIcon = new Label { Text = Theme.IconInfo, Font = new Font("Segoe MDL2 Assets", 16f), ForeColor = Theme.Accent, AutoSize = true, Dock = DockStyle.Top, Margin = Theme.ScalePadding(0, Theme.Sp1, Theme.Sp1, 0), BackColor = Color.Transparent };
        var infoText = new Label
        {
            Text = $"{T("Profile.PickEncrypted")}\n{T("Profile.AccountXuid")}\n{T("Profile.DecryptToView")}",
            Font = Theme.Small, ForeColor = Theme.TextMain, BackColor = Color.Transparent,
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
        };
        infoLayout.Controls.Add(infoIcon, 0, 0);
        infoLayout.Controls.Add(infoText, 1, 0);
        info.Controls.Add(infoLayout);
        col.Controls.Add(info, 0, 0);

        var card = new Card(T("Profile.Title"), Theme.IconUnlock) { Dock = DockStyle.Fill, Margin = Theme.ScalePadding(0, 0, 0, Theme.Sp2) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 7, BackColor = Theme.Panel };
        grid.ColumnStyles.Add(Theme.ScaleColumn(112));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(Theme.ScaleColumn(112));
        grid.RowStyles.Add(Theme.ScaleRow(24));
        grid.RowStyles.Add(Theme.ScaleRow(42));
        grid.RowStyles.Add(Theme.ScaleRow(50));
        grid.RowStyles.Add(Theme.ScaleRow(24));
        grid.RowStyles.Add(Theme.ScaleRow(42));
        grid.RowStyles.Add(Theme.ScaleRow(50));
        grid.RowStyles.Add(Theme.ScaleRow(44));

        grid.Controls.Add(MakeCaption(T("Profile.EncryptedProfile")), 0, 0);
        grid.SetColumnSpan(grid.GetControlFromPosition(0, 0)!, 3);
        _profileBox = MakeTextBox();
        grid.Controls.Add(_profileBox, 0, 1);
        grid.SetColumnSpan(_profileBox, 2);
        grid.Controls.Add(MakeBrowse(T("Common.Browse"), () => BrowseInto(_profileBox, "C_ProfileData (*.*)|*.*")), 2, 1);

        var decryptRow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Margin = Theme.ScalePadding(0, 4, 0, 0), WrapContents = true };
        _decryptButton = Theme.MakeButton(T("Profile.DecryptProfile"), Theme.IconUnlock, primary: true);
        _decryptButton.Width = Theme.Scaled(210); _decryptButton.Height = Theme.Scaled(38);
        _decryptButton.Click += async (_, _) => await DecryptAsync();
        decryptRow.Controls.Add(_decryptButton);
        grid.Controls.Add(decryptRow, 0, 2);
        grid.SetColumnSpan(decryptRow, 3);

        grid.Controls.Add(MakeCaption(T("Profile.AccountXuid")), 0, 3);
        grid.SetColumnSpan(grid.GetControlFromPosition(0, 3)!, 3);
        _xuidBox = MakeTextBox();
        _xuidBox.Enabled = false;
        grid.Controls.Add(_xuidBox, 0, 4);
        grid.SetColumnSpan(_xuidBox, 2);

        var xuidButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Margin = Theme.ScalePadding(0, 4, 0, 0), WrapContents = true };
        var launchXbox = Theme.MakeButton(T("Common.LaunchXbox"), Theme.IconContact);
        launchXbox.Width = Theme.Scaled(190); launchXbox.Height = Theme.Scaled(38);
        launchXbox.Click += (_, _) => { Logger.Info("Launching Xbox App."); XuidGrabber.LaunchXboxApp(); };
        _grabButton = Theme.MakeButton(T("Common.GrabXuid"), Theme.IconShield);
        _grabButton.Width = Theme.Scaled(160); _grabButton.Height = Theme.Scaled(38); _grabButton.Margin = Theme.ScalePadding(Theme.Sp1, 0, 0, 0);
        _grabButton.Enabled = false;
        _grabButton.Click += async (_, _) => await GrabXuidAsync();
        xuidButtons.Controls.Add(launchXbox);
        xuidButtons.Controls.Add(_grabButton);
        grid.Controls.Add(xuidButtons, 0, 5);
        grid.SetColumnSpan(xuidButtons, 3);

        _status = new Label
        {
            Dock = DockStyle.Fill, Font = Theme.Small, ForeColor = Theme.TextMuted, TextAlign = ContentAlignment.MiddleLeft,
            Text = T("Profile.PickEncrypted"),
        };
        grid.Controls.Add(_status, 0, 6);
        grid.SetColumnSpan(_status, 3);

        card.Controls.Add(grid);
        col.Controls.Add(card, 0, 1);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _saveButton = Theme.MakeButton(T("Profile.Encrypt"), Theme.IconShield, primary: true);
        _saveButton.Dock = DockStyle.Fill;
        _saveButton.Margin = Theme.ScalePadding(0, Theme.Sp1, Theme.Sp1, Theme.Sp1);
        _saveButton.Enabled = false;
        _saveButton.Click += async (_, _) => await ReencryptAndSaveAsync();
        var resetButton = Theme.MakeButton(T("Common.Reset"), Theme.IconClear);
        resetButton.Dock = DockStyle.Fill;
        resetButton.Margin = Theme.ScalePadding(0, Theme.Sp1, 0, Theme.Sp1);
        resetButton.Click += (_, _) => ResetState();
        actions.Controls.Add(_saveButton, 0, 0);
        actions.Controls.Add(resetButton, 1, 0);
        col.Controls.Add(actions, 0, 2);
        return col;
    }

    private Control BuildPreview()
    {
        var card = new Card(T("Profile.DecryptedProfile"), Theme.IconDatabase) { Dock = DockStyle.Fill, Margin = new Padding(0) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Panel };
        layout.RowStyles.Add(Theme.ScaleRow(44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _detected = new Label
        {
            Dock = DockStyle.Fill, Font = Theme.Body, ForeColor = Theme.TextMuted, TextAlign = ContentAlignment.MiddleLeft,
            Text = T("Profile.DecryptToView"), Padding = Theme.ScalePadding(Theme.Sp1, 0, 0, 0),
        };
        layout.Controls.Add(_detected, 0, 0);

        _fields = new DarkListView
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.BgSecondary,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
        };
        _fields.Columns.Add(T("Profile.Property"), Theme.Scaled(220));
        _fields.Columns.Add(T("Dash.Type"), Theme.Scaled(60));
        _fields.Columns.Add(T("Profile.Value"), Theme.Scaled(140));
        _fields.Columns.Add("", Theme.Scaled(56));
        _fields.Resize += (_, _) => ResizeFieldColumns();
        ResizeFieldColumns();
        _fields.DoubleClick += (_, _) => EditSelectedField();
        layout.Controls.Add(_fields, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private void ResizeFieldColumns()
    {
        if (_fields.Columns.Count < 4) return;
        int type = Theme.Scaled(60);
        int value = Theme.Scaled(140);
        int edit = Theme.Scaled(56);
        int available = Math.Max(Theme.Scaled(360), _fields.ClientSize.Width);
        int property = Math.Max(Theme.Scaled(160), available - type - value - edit);
        _fields.Columns[0].Width = property;
        _fields.Columns[1].Width = type;
        _fields.Columns[2].Width = value;
        _fields.Columns[3].Width = edit;
    }

    private void EditSelectedField()
    {
        if (_decryptedPlaintext is null) return;
        if (_fields.SelectedItems.Count == 0) return;
        if (_fields.SelectedItems[0].Tag is not Fh6ProfilePlaintext.Field f) return;
        if (!f.Editable)
        {
            SetStatus($"\"{f.Name}\" is display-only.", Theme.TextMuted);
            return;
        }
        var current = _fields.SelectedItems[0].SubItems[2].Text;
        long max = f.Width switch { 1 => 255, 4 => int.MaxValue, _ => long.MaxValue };
        var input = Prompt($"Edit \"{f.Name}\" ({f.Type}, max {max:N0}):", current);
        if (input is null) return;
        if (!long.TryParse(input.Trim(), out long newValue)) { Warn($"Enter a whole number ({f.Type})."); return; }
        if (f.Width == 1 && (newValue < 0 || newValue > 255)) { Warn("A byte value must be 0–255."); return; }
        if (f.Width == 4 && (newValue < int.MinValue || newValue > int.MaxValue)) { Warn("Value out of Int32 range."); return; }
        try
        {
            _decryptedPlaintext = Fh6ProfilePlaintext.PatchScalar(_decryptedPlaintext, f.ValueOffset, f.Width, newValue);
            _pendingEdits++;
            RenderFields(_decryptedPlaintext, _currentXuid);
            SetStatus($"Set {f.Name} = {newValue}. {_pendingEdits} edit(s) pending — Re-encrypt && Save to apply.", Theme.Success);
            Logger.Info($"Profile field edit: {f.Name} -> {newValue} @0x{f.ValueOffset:X} ({f.Width}B)");
        }
        catch (Exception ex) { Warn(ex.Message); }
    }

    private string? Prompt(string label, string initial)
    {
        using var dlg = new Form
        {
            Text = "Edit value", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            ClientSize = Theme.ScaleSize(390, 150), MaximizeBox = false, MinimizeBox = false, BackColor = Theme.Bg,
        };
        var lbl = new Label { Text = label, ForeColor = Theme.TextMain, Font = Theme.Body, AutoSize = true, Location = new Point(Theme.Scaled(14), Theme.Scaled(14)), MaximumSize = Theme.ScaleSize(360, 0) };
        var box = new TextBox { Text = initial, BackColor = Theme.BgSecondary, ForeColor = Theme.TextMain, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Body, Location = new Point(Theme.Scaled(14), Theme.Scaled(58)), Width = Theme.Scaled(362) };
        var ok = Theme.MakeButton("OK", Theme.IconShield, primary: true); ok.Location = new Point(Theme.Scaled(196), Theme.Scaled(104)); ok.Width = Theme.Scaled(84); ok.DialogResult = DialogResult.OK;
        var cancel = Theme.MakeButton("Cancel", Theme.IconClear); cancel.Location = new Point(Theme.Scaled(290), Theme.Scaled(104)); cancel.Width = Theme.Scaled(86); cancel.DialogResult = DialogResult.Cancel;
        dlg.Controls.AddRange(new Control[] { lbl, box, ok, cancel });
        dlg.AcceptButton = ok; dlg.CancelButton = cancel;
        return dlg.ShowDialog(this) == DialogResult.OK ? box.Text : null;
    }

    private async Task DecryptAsync()
    {
        var profile = _profileBox.Text.Trim();
        if (!File.Exists(profile)) { Warn("Select an encrypted C_ProfileData."); return; }

        _decryptButton.Enabled = false;
        SetStatus("Decrypting on backend…", Theme.Warning);
        Logger.Info("Profile decrypt started (IVs derived server-side).");
        try
        {

            var plaintext = await _backend.DecryptProfileWithIvsAsync(profile, Path.GetFileName(profile), "");
            if (!Fh6ProfilePlaintext.IsFh6Plaintext(plaintext))
            {
                SetStatus("Decrypt returned unexpected data.", Theme.Error);
                Logger.Warn("Decrypt output did not have the FH6 profile preamble.");
                return;
            }
            _decryptedPlaintext = plaintext;
            _pendingEdits = 0;

            var found = Fh6ProfilePlaintext.FindXuid(plaintext);
            _currentXuid = found?.Xuid;
            _xuidBox.Enabled = true;
            _grabButton.Enabled = true;
            _saveButton.Enabled = true;
            _xuidBox.Text = found is { } f ? f.Xuid.ToString() : "";

            RenderFields(plaintext, found?.Xuid);
            SetStatus($"Decrypted {plaintext.Length:N0} bytes. XUID {(found is null ? "not auto-detected" : found.Value.Xuid.ToString())}.", Theme.Success);
            Logger.Success($"Profile decrypted ({plaintext.Length} bytes).");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, Theme.Error);
            Logger.Exception("Profile decrypt failed", ex);
        }
        finally
        {
            _decryptButton.Enabled = true;
        }
    }

    private void RenderFields(byte[] plaintext, ulong? xuid)
    {
        _fields.BeginUpdate();
        _fields.Items.Clear();
        if (xuid is ulong x)
        {
            var item = new ListViewItem("XUID (account identity)") { ForeColor = Theme.Accent };
            item.SubItems.Add("LE64");
            item.SubItems.Add(x.ToString());
            item.SubItems.Add("← above");
            _fields.Items.Add(item);
        }
        int editableCount = 0;
        foreach (var field in Fh6ProfilePlaintext.ScanFields(plaintext))
        {
            var item = new ListViewItem(field.Name) { Tag = field };
            item.SubItems.Add(field.Type);
            item.SubItems.Add(field.Value);
            if (field.Editable) { item.SubItems.Add("✎ edit"); item.ForeColor = Theme.TextMain; editableCount++; }
            else { item.SubItems.Add(""); item.ForeColor = Theme.TextMuted; }
            _fields.Items.Add(item);
        }
        _detected.ForeColor = Theme.TextMain;
        _detected.Text = $"{plaintext.Length:N0} bytes • {_fields.Items.Count} properties • {editableCount} editable (double-click)"
                       + (xuid is null ? "" : $" • XUID 0x{xuid:X}");
        _fields.EndUpdate();
    }

    private async Task ReencryptAndSaveAsync()
    {
        if (_decryptedPlaintext is null) { Warn("Decrypt a profile first."); return; }
        if (!Fh6ProfilePlaintext.IsFh6Plaintext(_decryptedPlaintext)) { Warn("Loaded plaintext is not a valid FH6 profile."); return; }
        if (!Fh6ProfilePlaintext.TryParseXuidText(_xuidBox.Text, out ulong newXuid)) { Warn("Enter a valid XUID (decimal or 0x hex)."); return; }

        byte[] edited;
        try
        {
            if (_currentXuid is ulong cur && cur != newXuid)
                edited = Fh6ProfilePlaintext.PatchXuid(_decryptedPlaintext, cur, newXuid);
            else
                edited = _decryptedPlaintext;
        }
        catch (Exception ex)
        {
            Warn(ex.Message);
            return;
        }

        using var save = new SaveFileDialog
        {
            Title = "Save re-encrypted C_ProfileData",
            FileName = "C_ProfileData",
            Filter = "C_ProfileData (*.*)|*.*",
        };
        if (save.ShowDialog(this) != DialogResult.OK) return;

        _saveButton.Enabled = false;
        SetStatus("Re-encrypting on backend…", Theme.Warning);
        Logger.Info("Profile re-encrypt (IV-based) started.");
        var tmp = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tmp, edited);
            var reencrypted = await _backend.ReencryptProfileWithIvsAsync(tmp, "edited_profile.bin", "");
            await File.WriteAllBytesAsync(save.FileName, reencrypted);
            SetStatus($"Saved {reencrypted.Length:N0} bytes → {Path.GetFileName(save.FileName)}", Theme.Success);
            Logger.Success($"Re-encrypted profile written to {save.FileName} ({reencrypted.Length} bytes).");
            MessageBox.Show(
                "Re-encrypted profile saved.\n\nTo use it: CLOSE Forza Horizon 6, copy this file over your "
                + "C_ProfileData in the PGS container, then launch the game. Do not let FH6 autosave between "
                + "capturing IVs and swapping the file.",
                "Profile saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, Theme.Error);
            Logger.Exception("Profile re-encrypt failed", ex);
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
            _saveButton.Enabled = true;
        }
    }

    private async Task GrabXuidAsync()
    {
        _grabButton.Enabled = false;
        SetStatus("Looking for the Xbox App…", Theme.Warning);
        try
        {
            void Status(string s) => BeginInvoke(() => SetStatus(s, Theme.Warning));
            var xuid = await Task.Run(() => XuidGrabber.GrabAsync(Status));
            _xuidBox.Text = xuid.ToString();
            SetStatus($"XUID grabbed: {xuid}", Theme.Success);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, Theme.Error);
            Logger.Warn($"Grab XUID failed: {ex.Message}");
        }
        finally
        {
            _grabButton.Enabled = true;
        }
    }

    private void ResetState()
    {
        _profileBox.Clear(); _xuidBox.Clear();
        _decryptedPlaintext = null; _currentXuid = null; _pendingEdits = 0;
        _xuidBox.Enabled = false; _grabButton.Enabled = false; _saveButton.Enabled = false;
        _fields.Items.Clear();
        _detected.Text = "Decrypt a profile to view its properties."; _detected.ForeColor = Theme.TextMuted;
        SetStatus("Pick an encrypted profile + its IV table, then Decrypt.", Theme.TextMuted);
    }

    private void SetStatus(string text, Color color) { _status.Text = text; _status.ForeColor = color; }
    private void Warn(string message) { Logger.Warn(message); MessageBox.Show(message, "Profile Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning); }

    private static Label MakeCaption(string text) => new()
    { Text = text, Font = Theme.Small, ForeColor = Theme.TextMuted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft };

    private static TextBox MakeTextBox() => new()
    { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.BgSecondary, ForeColor = Theme.TextMain, Font = Theme.Body, Margin = Theme.ScalePadding(0, 4, Theme.Sp1, 4) };

    private static Button MakeBrowse(string text, Action onClick)
    {
        var b = Theme.MakeButton(text);
        b.TextAlign = ContentAlignment.MiddleCenter; b.Padding = new Padding(0);
        b.Dock = DockStyle.Fill; b.Margin = Theme.ScalePadding(0, 4, 0, 4);
        b.Click += (_, _) => onClick();
        return b;
    }

    private void BrowseInto(TextBox target, string filter)
    {
        using var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.FileName;
    }
}
