using System.Text.Json;

namespace ForzaCryptoTool;

internal sealed class ForzaTechView : UserControl
{
    private readonly BackendClient _backend;
    private readonly AppSettings _settings;

    private static readonly (string Value, string Label)[] Games =
    {
        ("fm6apex", "Forza Motorsport 6: Apex"),
        ("fh3", "Forza Horizon 3"),
        ("fm7", "Forza Motorsport 7"),
        ("fh4", "Forza Horizon 4"),
        ("fh5", "Forza Horizon 5"),
    };

    private static readonly Dictionary<string, string[]> KeyTypesByGame = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fm6apex"] = new[] { "configfile", "profile", "photo", "gamedb" },
        ["fh3"]     = new[] { "file", "profile", "photo", "gamedb", "sfs" },
        ["fm7"]     = new[] { "configfile", "profile", "reward", "photo", "gamedb", "sfs" },
        ["fh4"]     = new[] { "file", "profile", "photo", "dynamic", "gamedb", "sfs" },
        ["fh5"]     = new[] { "file", "profile", "photo", "dynamic", "gamedb", "sfs" },
    };

    private string[] KeyTypesForCurrentGame()
        => KeyTypesByGame.TryGetValue(SelectedGame(), out var k) ? k : Array.Empty<string>();

    private ComboBox _gameBox = null!;
    private ComboBox _keyTypeBox = null!;

    private TextBox _decInputBox = null!;
    private Button _decryptButton = null!;
    private Label _decStatus = null!;

    private TextBox _reEditedBox = null!;
    private TextBox _reOriginalBox = null!;
    private Button _reencryptButton = null!;
    private Label _reStatus = null!;

    public ForzaTechView(BackendClient backend, AppSettings settings)
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
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg, Padding = Theme.ScalePadding(Theme.Sp3) };
        var col = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Theme.Bg,
            Width = Theme.Scaled(980),
        };
        var cards = new List<Control>();
        void ResizeContent()
        {
            int width = Math.Max(Theme.Scaled(560),
                scroll.ClientSize.Width - scroll.Padding.Left - scroll.Padding.Right - SystemInformation.VerticalScrollBarWidth);
            col.Width = width;
            foreach (var card in cards)
                card.Width = Math.Max(Theme.Scaled(540), width - Theme.Scaled(4));
        }

        var selCard = new Card(T("ForzaTech.Target"), Theme.IconDatabase) { Width = Theme.Scaled(960), Height = Theme.Scaled(120), Margin = Theme.ScalePadding(0, 0, 0, Theme.Sp2) };
        cards.Add(selCard);
        var selGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Theme.Panel };
        selGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        selGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        selGrid.RowStyles.Add(Theme.ScaleRow(24));
        selGrid.RowStyles.Add(Theme.ScaleRow(42));
        selGrid.Controls.Add(MakeCaption(T("ForzaTech.Game")), 0, 0);
        selGrid.Controls.Add(MakeCaption(T("ForzaTech.KeyType")), 1, 0);
        _gameBox = MakeCombo();
        foreach (var (_, label) in Games) _gameBox.Items.Add(label);
        selGrid.Controls.Add(_gameBox, 0, 1);
        _keyTypeBox = MakeCombo();
        selGrid.Controls.Add(_keyTypeBox, 1, 1);

        _gameBox.SelectedIndexChanged += (_, _) => PopulateKeyTypes();
        _gameBox.SelectedIndex = 4;
        selCard.Controls.Add(selGrid);
        col.Controls.Add(selCard);

        var decCard = new Card(T("Dash.Decrypt"), Theme.IconUnlock) { Width = Theme.Scaled(960), Height = Theme.Scaled(176), Margin = Theme.ScalePadding(0, 0, 0, Theme.Sp2) };
        cards.Add(decCard);
        var decGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4, BackColor = Theme.Panel };
        decGrid.ColumnStyles.Add(Theme.ScaleColumn(112));
        decGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        decGrid.ColumnStyles.Add(Theme.ScaleColumn(112));
        decGrid.RowStyles.Add(Theme.ScaleRow(24));
        decGrid.RowStyles.Add(Theme.ScaleRow(42));
        decGrid.RowStyles.Add(Theme.ScaleRow(50));
        decGrid.RowStyles.Add(Theme.ScaleRow(30));
        decGrid.Controls.Add(MakeCaption(T("ForzaTech.EncryptedFile")), 0, 0);
        decGrid.SetColumnSpan(decGrid.GetControlFromPosition(0, 0)!, 3);
        _decInputBox = MakeTextBox();
        decGrid.Controls.Add(_decInputBox, 0, 1);
        decGrid.SetColumnSpan(_decInputBox, 2);
        decGrid.Controls.Add(MakeBrowse(T("Common.Browse"), () => BrowseInto(_decInputBox)), 2, 1);
        var decRow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Margin = Theme.ScalePadding(0, 4, 0, 0), WrapContents = true };
        _decryptButton = Theme.MakeButton(T("Dash.Decrypt"), Theme.IconUnlock, primary: true);
        _decryptButton.Width = Theme.Scaled(160); _decryptButton.Height = Theme.Scaled(38);
        _decryptButton.Click += async (_, _) => await DecryptAsync();
        decRow.Controls.Add(_decryptButton);
        decGrid.Controls.Add(decRow, 0, 2);
        decGrid.SetColumnSpan(decRow, 3);
        _decStatus = MakeStatus(T("ForzaTech.PickDecrypt"));
        decGrid.Controls.Add(_decStatus, 0, 3);
        decGrid.SetColumnSpan(_decStatus, 3);
        decCard.Controls.Add(decGrid);
        col.Controls.Add(decCard);

        var reCard = new Card(T("Dash.ReEncrypt"), Theme.IconShield) { Width = Theme.Scaled(960), Height = Theme.Scaled(280), Margin = Theme.ScalePadding(0, 0, 0, Theme.Sp2) };
        cards.Add(reCard);
        var reGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 6, BackColor = Theme.Panel };
        reGrid.ColumnStyles.Add(Theme.ScaleColumn(112));
        reGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        reGrid.ColumnStyles.Add(Theme.ScaleColumn(112));
        for (int i = 0; i < 5; i++) reGrid.RowStyles.Add(Theme.ScaleRow(i % 2 == 0 ? 24 : 42));
        reGrid.RowStyles.Add(Theme.ScaleRow(50));
        reGrid.Controls.Add(MakeCaption(T("ForzaTech.EditedPlaintext")), 0, 0);
        reGrid.SetColumnSpan(reGrid.GetControlFromPosition(0, 0)!, 3);
        _reEditedBox = MakeTextBox();
        reGrid.Controls.Add(_reEditedBox, 0, 1);
        reGrid.SetColumnSpan(_reEditedBox, 2);
        reGrid.Controls.Add(MakeBrowse(T("Common.Browse"), () => BrowseInto(_reEditedBox)), 2, 1);
        reGrid.Controls.Add(MakeCaption(T("ForzaTech.OriginalEncrypted")), 0, 2);
        reGrid.SetColumnSpan(reGrid.GetControlFromPosition(0, 2)!, 3);
        _reOriginalBox = MakeTextBox();
        reGrid.Controls.Add(_reOriginalBox, 0, 3);
        reGrid.SetColumnSpan(_reOriginalBox, 2);
        reGrid.Controls.Add(MakeBrowse(T("Common.Browse"), () => BrowseInto(_reOriginalBox)), 2, 3);
        _reStatus = MakeStatus(T("ForzaTech.PickReencrypt"));
        reGrid.Controls.Add(_reStatus, 0, 4);
        reGrid.SetColumnSpan(_reStatus, 3);
        var reRow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Margin = Theme.ScalePadding(0, 4, 0, 0), WrapContents = true };
        _reencryptButton = Theme.MakeButton(T("Dash.ReEncrypt"), Theme.IconShield, primary: true);
        _reencryptButton.Width = Theme.Scaled(170); _reencryptButton.Height = Theme.Scaled(38);
        _reencryptButton.Click += async (_, _) => await ReencryptAsync();
        reRow.Controls.Add(_reencryptButton);
        reGrid.Controls.Add(reRow, 0, 5);
        reGrid.SetColumnSpan(reRow, 3);
        reCard.Controls.Add(reGrid);
        col.Controls.Add(reCard);

        scroll.Controls.Add(col);
        Controls.Add(scroll);
        scroll.Resize += (_, _) => ResizeContent();
        ResizeContent();
    }

    private string SelectedGame() => Games[Math.Max(0, _gameBox.SelectedIndex)].Value;
    private string SelectedKeyType() => _keyTypeBox.SelectedItem?.ToString() ?? "";

    private void PopulateKeyTypes()
    {
        var previous = _keyTypeBox.SelectedItem?.ToString();
        var types = KeyTypesForCurrentGame();
        _keyTypeBox.BeginUpdate();
        _keyTypeBox.Items.Clear();
        foreach (var k in types) _keyTypeBox.Items.Add(k);
        _keyTypeBox.EndUpdate();
        if (types.Length == 0) return;
        int idx = previous is not null ? Array.FindIndex(types, t => string.Equals(t, previous, StringComparison.OrdinalIgnoreCase)) : -1;
        if (idx < 0) { idx = Array.FindIndex(types, t => t == "gamedb"); if (idx < 0) idx = 0; }
        _keyTypeBox.SelectedIndex = idx;
    }

    private async Task DecryptAsync()
    {
        var input = _decInputBox.Text.Trim();
        if (!File.Exists(input)) { Warn("Select an encrypted file to decrypt."); return; }

        using var save = new SaveFileDialog
        {
            Title = "Save decrypted file",
            FileName = Path.GetFileName(input) + ".decrypted",
            Filter = "All files (*.*)|*.*",
        };
        if (save.ShowDialog(this) != DialogResult.OK) return;

        _decryptButton.Enabled = false;
        SetStatus(_decStatus, "Decrypting on backend…", Theme.Warning);
        Logger.Info($"ForzaTech decrypt: game={SelectedGame()} key_type={SelectedKeyType()} file={Path.GetFileName(input)}");
        try
        {
            using var json = await _backend.ForzaTechDecryptAsync(input, Path.GetFileName(input), SelectedGame(), SelectedKeyType());
            var jobId = BackendClient.FindString(json.RootElement, "job_id", "jobId", "id");
            if (string.IsNullOrWhiteSpace(jobId))
                throw new InvalidOperationException("Backend response did not include a job_id.");
            ShowWarnings(_decStatus, json.RootElement);
            await _backend.DownloadAsync(jobId!, "forzatech-decrypted", save.FileName);
            SetStatus(_decStatus, $"Saved {Path.GetFileName(save.FileName)}. {WarningSuffix(json.RootElement)}", Theme.Success);
            Logger.Success($"ForzaTech decrypted → {save.FileName}");
        }
        catch (Exception ex)
        {
            SetStatus(_decStatus, ex.Message, Theme.Error);
            Logger.Exception("ForzaTech decrypt failed", ex);
        }
        finally { _decryptButton.Enabled = true; }
    }

    private async Task ReencryptAsync()
    {
        var edited = _reEditedBox.Text.Trim();
        var original = _reOriginalBox.Text.Trim();
        if (!File.Exists(edited)) { Warn("Select the edited plaintext file."); return; }
        if (!File.Exists(original)) { Warn("Select the original encrypted file (needed for framing/IVs)."); return; }

        using var save = new SaveFileDialog
        {
            Title = "Save re-encrypted file",
            FileName = Path.GetFileName(original) + ".reencrypted",
            Filter = "All files (*.*)|*.*",
        };
        if (save.ShowDialog(this) != DialogResult.OK) return;

        _reencryptButton.Enabled = false;
        SetStatus(_reStatus, "Re-encrypting on backend…", Theme.Warning);
        Logger.Info($"ForzaTech re-encrypt: game={SelectedGame()} key_type={SelectedKeyType()}");
        try
        {
            using var json = await _backend.ForzaTechReencryptAsync(
                edited, Path.GetFileName(edited), original, SelectedGame(), SelectedKeyType());
            var jobId = BackendClient.FindString(json.RootElement, "job_id", "jobId", "id");
            if (string.IsNullOrWhiteSpace(jobId))
                throw new InvalidOperationException("Backend response did not include a job_id.");
            ShowWarnings(_reStatus, json.RootElement);
            await _backend.DownloadAsync(jobId!, "forzatech-reencrypted", save.FileName);
            SetStatus(_reStatus, $"Saved {Path.GetFileName(save.FileName)}. {WarningSuffix(json.RootElement)}", Theme.Success);
            Logger.Success($"ForzaTech re-encrypted → {save.FileName}");
        }
        catch (Exception ex)
        {
            SetStatus(_reStatus, ex.Message, Theme.Error);
            Logger.Exception("ForzaTech re-encrypt failed", ex);
        }
        finally { _reencryptButton.Enabled = true; }
    }

    private static void ShowWarnings(Label status, JsonElement root)
    {
        var warn = BackendClient.FindString(root, "warning", "warnings", "message", "detail");
        if (!string.IsNullOrWhiteSpace(warn))
            Logger.Warn($"ForzaTech backend warning: {warn}");
    }

    private static string WarningSuffix(JsonElement root)
    {
        var warn = BackendClient.FindString(root, "warning", "warnings", "message", "detail");
        return string.IsNullOrWhiteSpace(warn) ? "" : $"⚠ {warn}";
    }

    private static Label MakeCaption(string text) => new()
    { Text = text, Font = Theme.Small, ForeColor = Theme.TextMuted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft };

    private static Label MakeStatus(string text) => new()
    { Dock = DockStyle.Fill, Font = Theme.Small, ForeColor = Theme.TextMuted, TextAlign = ContentAlignment.MiddleLeft, Text = text };

    private static TextBox MakeTextBox() => new()
    { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.BgSecondary, ForeColor = Theme.TextMain, Font = Theme.Body, Margin = Theme.ScalePadding(0, 4, Theme.Sp1, 4) };

    private static ComboBox MakeCombo() => new DarkComboBox
    { Dock = DockStyle.Fill, Margin = Theme.ScalePadding(0, 4, Theme.Sp1, 4) };

    private static Button MakeBrowse(string text, Action onClick)
    {
        var b = Theme.MakeButton(text);
        b.TextAlign = ContentAlignment.MiddleCenter; b.Padding = new Padding(0);
        b.Dock = DockStyle.Fill; b.Margin = Theme.ScalePadding(0, 4, 0, 4);
        b.Click += (_, _) => onClick();
        return b;
    }

    private void BrowseInto(TextBox target)
    {
        using var dialog = new OpenFileDialog { Filter = "All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.FileName;
    }

    private static void SetStatus(Label status, string text, Color color) { status.Text = text; status.ForeColor = color; }
    private void Warn(string message) { Logger.Warn(message); MessageBox.Show(message, "Older ForzaTech", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
}
