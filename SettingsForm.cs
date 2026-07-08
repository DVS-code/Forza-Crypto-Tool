namespace ForzaCryptoTool;

internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly BackendClient _backend;

    private TextBox _endpointBox = null!;
    private Label _endpointSource = null!;
    private TextBox _apiKeyBox = null!;
    private TextBox _outputBox = null!;
    private TextBox _storageOutputBox = null!;
    private Label _statusBadge = null!;
    private Label _latencyValue = null!;
    private Label _lastCheckValue = null!;
    private CheckBox _autoUpdate = null!;
    private DarkComboBox _languageBox = null!;
    private readonly System.Windows.Forms.Timer _reloadFadeTimer = new() { Interval = 15 };
    private bool _buildingUi;

    public event Action? CheckForUpdatesRequested;
    public event Action? LanguageChanged;

    public SettingsForm(AppSettings settings, BackendClient backend)
    {
        _settings = settings;
        _backend = backend;
        _reloadFadeTimer.Tick += (_, _) =>
        {
            Opacity = Math.Min(1.0, Opacity + 0.08);
            if (Opacity >= 1.0)
                _reloadFadeTimer.Stop();
        };
        BuildUi();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyDarkTitleBar(this);
    }

    private void BuildUi()
    {
        _buildingUi = true;
        SuspendLayout();
        Controls.Clear();

        Text = T("App.SettingsTitle", BuildConfig.AppName);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        ClientSize = Theme.ScaleSize(640, 740);
        BackColor = Theme.Bg;
        Font = Theme.Body;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Bg };
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(Theme.ScaleRow(64));
        Controls.Add(outer);

        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.Bg,
            Padding = Theme.ScalePadding(Theme.Sp3, Theme.Sp3, Theme.Sp3, 0),
        };
        outer.Controls.Add(scroll, 0, 0);

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Theme.Bg,
        };
        root.RowStyles.Add(Theme.ScaleRow(220));
        root.RowStyles.Add(Theme.ScaleRow(116));
        root.RowStyles.Add(Theme.ScaleRow(116));
        root.RowStyles.Add(Theme.ScaleRow(132));
        root.RowStyles.Add(Theme.ScaleRow(156));
        scroll.Controls.Add(root);
        scroll.Resize += (_, _) =>
        {
            root.Width = Math.Max(Theme.Scaled(520),
                scroll.ClientSize.Width - scroll.Padding.Left - scroll.Padding.Right - SystemInformation.VerticalScrollBarWidth);
        };
        root.Width = Math.Max(Theme.Scaled(520),
            scroll.ClientSize.Width - scroll.Padding.Left - scroll.Padding.Right - SystemInformation.VerticalScrollBarWidth);

        var backend = new Card(T("Settings.Backend"), Theme.IconSettings) { Dock = DockStyle.Fill, Margin = Theme.ScalePadding(0, 0, 0, Theme.Sp2) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4, BackColor = Theme.Panel };
        grid.ColumnStyles.Add(Theme.ScaleColumn(126));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(Theme.ScaleColumn(160));
        grid.RowStyles.Add(Theme.ScaleRow(40));
        grid.RowStyles.Add(Theme.ScaleRow(20));
        grid.RowStyles.Add(Theme.ScaleRow(40));
        grid.RowStyles.Add(Theme.ScaleRow(40));

        grid.Controls.Add(Theme.MakeLabel(T("Settings.Endpoint")), 0, 0);
        _endpointBox = MakeTextBox(_backend.MaskedEndpoint);
        _endpointBox.ReadOnly = !BuildConfig.AllowCustomEndpoint;
        if (!BuildConfig.AllowCustomEndpoint) _endpointBox.ForeColor = Theme.TextMuted;
        grid.Controls.Add(_endpointBox, 1, 0);
        var testButton = Theme.MakeButton(T("Settings.TestConnection"), Theme.IconContact);
        testButton.Margin = Theme.ScalePadding(Theme.Sp1, 4, 0, 4); testButton.Dock = DockStyle.Fill;
        testButton.Click += async (_, _) => await TestConnectionAsync(testButton);
        grid.Controls.Add(testButton, 2, 0);

        _endpointSource = new Label
        {
            Text = T("Settings.Source", _backend.EndpointSource),
            Font = Theme.Small, ForeColor = Theme.TextMuted, AutoSize = false, Dock = DockStyle.Fill,
        };
        grid.Controls.Add(_endpointSource, 1, 1);
        grid.SetColumnSpan(_endpointSource, 2);

        grid.Controls.Add(Theme.MakeLabel(T("Settings.AppKey")), 0, 2);
        _apiKeyBox = MakeTextBox(_backend.MaskedApiKey);
        _apiKeyBox.UseSystemPasswordChar = false;
        grid.Controls.Add(_apiKeyBox, 1, 2);
        var clearKeyButton = Theme.MakeButton(T("Settings.ClearKey"), Theme.IconClear);
        clearKeyButton.Margin = Theme.ScalePadding(Theme.Sp1, 4, 0, 4); clearKeyButton.Dock = DockStyle.Fill;
        clearKeyButton.Click += (_, _) =>
        {
            SecureConfig.ClearUserApiKey();
            _backend.Reload();
            _apiKeyBox.Text = _backend.MaskedApiKey;
            Logger.Info("Stored app key cleared.");
        };
        grid.Controls.Add(clearKeyButton, 2, 2);

        grid.Controls.Add(Theme.MakeLabel(T("Settings.OutputFolder")), 0, 3);
        _outputBox = MakeTextBox(_settings.OutputFolder);
        grid.Controls.Add(_outputBox, 1, 3);
        var browseButton = Theme.MakeButton(T("Settings.Browse"), Theme.IconFolder);
        browseButton.Margin = Theme.ScalePadding(Theme.Sp1, 4, 0, 4); browseButton.Dock = DockStyle.Fill;
        browseButton.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { SelectedPath = _outputBox.Text };
            if (dialog.ShowDialog(this) == DialogResult.OK) _outputBox.Text = dialog.SelectedPath;
        };
        grid.Controls.Add(browseButton, 2, 3);
        backend.Controls.Add(grid);
        root.Controls.Add(backend, 0, 0);

        var storage = new Card(T("Settings.Storage"), Theme.IconFolder) { Dock = DockStyle.Fill, Margin = Theme.ScalePadding(0, 0, 0, Theme.Sp2) };
        var sg = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Theme.Panel, Padding = Theme.ScalePadding(0, 2, 0, 0) };
        sg.ColumnStyles.Add(Theme.ScaleColumn(126));
        sg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sg.ColumnStyles.Add(Theme.ScaleColumn(160));
        sg.RowStyles.Add(Theme.ScaleRow(34));
        sg.Controls.Add(Theme.MakeLabel(T("Settings.Output")), 0, 0);
        _storageOutputBox = MakeTextBox(_settings.OutputFolder);
        sg.Controls.Add(_storageOutputBox, 1, 0);
        var outputBrowse = Theme.MakeButton(T("Settings.Browse"), Theme.IconFolder);
        outputBrowse.Dock = DockStyle.Fill; outputBrowse.Margin = Theme.ScalePadding(Theme.Sp1, 3, 0, 3);
        outputBrowse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { SelectedPath = _storageOutputBox.Text };
            if (dialog.ShowDialog(this) == DialogResult.OK) { _storageOutputBox.Text = dialog.SelectedPath; _outputBox.Text = dialog.SelectedPath; }
        };
        sg.Controls.Add(outputBrowse, 2, 0);
        storage.Controls.Add(sg);
        root.Controls.Add(storage, 0, 1);

        var languageCard = new Card(T("Settings.LanguageCard"), Theme.IconHistory) { Dock = DockStyle.Fill, Margin = Theme.ScalePadding(0, 0, 0, Theme.Sp2) };
        var languageRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Panel, Margin = new Padding(0) };
        languageRow.ColumnStyles.Add(Theme.ScaleColumn(126));
        languageRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        languageRow.RowStyles.Add(Theme.ScaleRow(34));
        languageRow.Controls.Add(Theme.MakeLabel(T("Settings.Language")), 0, 0);
        _languageBox = new DarkComboBox { Dock = DockStyle.Fill, Height = Theme.Scaled(30), Margin = Theme.ScalePadding(0, 1, Theme.Sp1, 1) };
        foreach (var language in Localization.SupportedLanguages)
            _languageBox.Items.Add(language.DisplayName);
        _languageBox.SelectedIndex = Math.Max(0, Array.FindIndex(Localization.SupportedLanguages,
            language => string.Equals(language.Code, _settings.LanguageCode, StringComparison.OrdinalIgnoreCase)));
        _languageBox.SelectedIndexChanged += (_, _) => ChangeLanguageFromSelection();
        languageRow.Controls.Add(_languageBox, 1, 0);
        languageCard.Controls.Add(languageRow);
        root.Controls.Add(languageCard, 0, 2);

        var updates = new Card(T("Settings.Updates"), Theme.IconHistory) { Dock = DockStyle.Fill, Margin = Theme.ScalePadding(0, 0, 0, Theme.Sp2) };
        var ug = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Theme.Panel, Padding = Theme.ScalePadding(0, 2, 0, 0) };
        ug.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ug.ColumnStyles.Add(Theme.ScaleColumn(140));
        ug.RowStyles.Add(Theme.ScaleRow(30));
        ug.RowStyles.Add(Theme.ScaleRow(24));
        _autoUpdate = new AccentCheckBox
        {
            Text = T("Settings.AutoUpdates"),
            Checked = _settings.AutoUpdateCheck, ForeColor = Theme.TextMain, BackColor = Color.Transparent,
            Font = Theme.Body, Height = Theme.Scaled(26), Width = Theme.Scaled(390), Anchor = AnchorStyles.Left, AutoCheck = true,
            Margin = new Padding(0),
        };
        ug.Controls.Add(_autoUpdate, 0, 0);
        var versionSub = new Label
        {
            Text = T("Settings.CurrentVersion", BuildConfig.AppVersion),
            Font = Theme.Small, ForeColor = Theme.TextMuted, AutoSize = true, Anchor = AnchorStyles.Left,
            BackColor = Color.Transparent, Margin = Theme.ScalePadding(Theme.Sp2, 0, 0, 0),
        };
        ug.Controls.Add(versionSub, 0, 1);
        var checkNow = Theme.MakeButton(T("Settings.CheckNow"), Theme.IconHistory);
        checkNow.Height = Theme.Scaled(30); checkNow.Anchor = AnchorStyles.Right; checkNow.Width = Theme.Scaled(130);
        checkNow.Margin = Theme.ScalePadding(Theme.Sp1, 0, 0, 0);
        checkNow.Click += (_, _) =>
        {

            _settings.AutoUpdateCheck = _autoUpdate.Checked;
            _settings.SkippedUpdateVersion = null;
            _settings.Save();
            CheckForUpdatesRequested?.Invoke();
        };
        ug.Controls.Add(checkNow, 1, 0);
        ug.SetRowSpan(checkNow, 2);
        updates.Controls.Add(ug);
        root.Controls.Add(updates, 0, 3);

        var health = new Card(T("Settings.Advanced"), Theme.IconShield) { Dock = DockStyle.Fill, Margin = Theme.ScalePadding(0, 0, 0, Theme.Sp2) };
        var hg = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, BackColor = Theme.Panel };
        hg.ColumnStyles.Add(Theme.ScaleColumn(150));
        hg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) hg.RowStyles.Add(Theme.ScaleRow(34));
        hg.Controls.Add(Theme.MakeLabel(T("Settings.ConnectionStatus")), 0, 0);
        _statusBadge = Theme.MakeBadge();
        Theme.SetBadge(_statusBadge, T("Settings.NotChecked"), Theme.TextMuted);
        hg.Controls.Add(_statusBadge, 1, 0);
        hg.Controls.Add(Theme.MakeLabel(T("Settings.Latency")), 0, 1);
        _latencyValue = Theme.MakeValue("-");
        hg.Controls.Add(_latencyValue, 1, 1);
        hg.Controls.Add(Theme.MakeLabel(T("Settings.LastHealthCheck")), 0, 2);
        _lastCheckValue = Theme.MakeValue("-");
        hg.Controls.Add(_lastCheckValue, 1, 2);
        health.Controls.Add(hg);
        root.Controls.Add(health, 0, 4);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = Theme.Bg,
            Padding = Theme.ScalePadding(0, 0, Theme.Sp3, 0),
        };
        var save = Theme.MakeButton(T("Settings.Save"), Theme.IconCheck, primary: true);
        save.Width = Theme.Scaled(120); save.Margin = Theme.ScalePadding(Theme.Sp1, Theme.Sp1, 0, 0);
        save.Click += (_, _) => OnSave();
        var cancel = Theme.MakeButton(T("Settings.Cancel"));
        cancel.Width = Theme.Scaled(110); cancel.Margin = Theme.ScalePadding(Theme.Sp1, Theme.Sp1, 0, 0);
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        footer.Controls.Add(save);
        footer.Controls.Add(cancel);
        if (BuildConfig.AllowCustomEndpoint)
        {
            var reset = Theme.MakeButton(T("Settings.ResetEndpoint"));
            reset.Width = Theme.Scaled(150); reset.Margin = Theme.ScalePadding(Theme.Sp1, Theme.Sp1, 0, 0);
            reset.Click += (_, _) =>
            {
                SecureConfig.ClearUserEndpoint();
                _backend.Reload();
                _endpointBox.Text = _backend.MaskedEndpoint;
                Logger.Info("Backend endpoint reset to embedded default.");
            };
            footer.Controls.Add(reset);
        }
        outer.Controls.Add(footer, 0, 1);

        AcceptButton = save;
        CancelButton = cancel;
        _buildingUi = false;
        ResumeLayout(true);
    }

    private void OnSave()
    {
        _settings.OutputFolder = (_storageOutputBox ?? _outputBox).Text.Trim();
        _settings.AutoUpdateCheck = _autoUpdate.Checked;
        if (_languageBox.SelectedIndex >= 0 && _languageBox.SelectedIndex < Localization.SupportedLanguages.Length)
            _settings.LanguageCode = Localization.SupportedLanguages[_languageBox.SelectedIndex].Code;
        _settings.Save();

        if (BuildConfig.AllowCustomEndpoint)
        {
            var entered = _endpointBox.Text.Trim();

            if (!string.IsNullOrWhiteSpace(entered) && entered.Contains("://") && !entered.Contains('•'))
            {
                SecureConfig.SaveUserEndpoint(entered);
                Logger.Info("Backend endpoint updated (stored DPAPI-encrypted).");
            }
        }

        var keyEntered = _apiKeyBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(keyEntered) && !keyEntered.Contains('•') && !keyEntered.StartsWith('('))
        {
            SecureConfig.SaveUserApiKey(keyEntered);
            Logger.Info("App key updated (stored DPAPI-encrypted).");
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void ChangeLanguageFromSelection()
    {
        if (_buildingUi || _languageBox.SelectedIndex < 0 || _languageBox.SelectedIndex >= Localization.SupportedLanguages.Length)
            return;

        _settings.OutputFolder = (_storageOutputBox ?? _outputBox).Text.Trim();
        _settings.AutoUpdateCheck = _autoUpdate.Checked;
        _settings.LanguageCode = Localization.SupportedLanguages[_languageBox.SelectedIndex].Code;
        _settings.Save();
        BeginReloadFade();
        LanguageChanged?.Invoke();
        BuildUi();
    }

    private void BeginReloadFade()
    {
        _reloadFadeTimer.Stop();
        Opacity = 0.78;
        _reloadFadeTimer.Start();
    }

    private static TextBox MakeTextBox(string text) => new()
    {
        Text = text, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.BgSecondary,
        ForeColor = Theme.TextMain, Font = Theme.Body, Margin = Theme.ScalePadding(0, 6, 0, 6),
    };

    private async Task TestConnectionAsync(Button button)
    {
        button.Enabled = false;
        Theme.SetBadge(_statusBadge, T("Settings.Checking"), Theme.Warning);
        try
        {

            if (BuildConfig.AllowCustomEndpoint)
            {
                var entered = _endpointBox.Text.Trim();
                if (!string.IsNullOrWhiteSpace(entered) && entered.Contains("://") && !entered.Contains('•'))
                {
                    SecureConfig.SaveUserEndpoint(entered);
                    _backend.Reload();
                }
            }
            var (ok, ms) = await _backend.CheckHealthAsync();
            if (ok)
            {
                Theme.SetBadge(_statusBadge, T("Settings.Connected"), Theme.Success);
                _latencyValue.Text = $"{ms} ms";
            }
            else
            {
                Theme.SetBadge(_statusBadge, T("Settings.Unreachable"), Theme.Error);
                _latencyValue.Text = "-";
            }
        }
        catch (Exception ex)
        {
            Theme.SetBadge(_statusBadge, T("Settings.Offline"), Theme.Error);
            _latencyValue.Text = "-";
            _lastCheckValue.Text = $"{DateTime.Now:HH:mm:ss} - {ex.Message}";
            button.Enabled = true;
            return;
        }
        _lastCheckValue.Text = DateTime.Now.ToString("HH:mm:ss");
        button.Enabled = true;
    }

    private string T(string key, params object[] args) => Localization.T(_settings, key, args);
}
