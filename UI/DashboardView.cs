using System.Diagnostics;
using System.Text.Json;

namespace ForzaCryptoTool;

internal sealed class DashboardView : UserControl
{
    private readonly BackendClient _backend;
    private readonly AppSettings _settings;
    private readonly ToolTip _tips = new();
    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = 2000 };

    private DropZone _dropZone = null!;
    private TableLayoutPanel _fileInfoGrid = null!;
    private Label _fileInfoEmpty = null!;
    private Label _typeBadge = null!, _encryptionBadge = null!, _integrityBadge = null!;
    private Label _fileNameValue = null!, _fileSizeValue = null!, _fileModifiedValue = null!;
    private Button _decryptButton = null!, _reencryptButton = null!, _mergeButton = null!, _exportButton = null!, _openOutputButton = null!;
    private Label _stepLabel = null!, _percentLabel = null!;
    private SlimProgress _progress = null!;
    private TableLayoutPanel _resultsGrid = null!;
    private Label _resultsEmpty = null!, _resultMode = null!, _resultOutput = null!, _resultIntegrity = null!, _resultWarnings = null!;
    private ListBox _recentList = null!;
    private RichTextBox _logBox = null!;

    private DetectionResult? _current;
    private string? _jobId;
    private string? _pendingDownload;

    private string? _lastOriginalEncryptedPath;

    private string? _lastOutputPath;
    private bool _busy;
    private int _pollProgress;

    public DashboardView(BackendClient backend, AppSettings settings)
    {
        _backend = backend;
        _settings = settings;
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        BuildUi();
        Logger.UiSink = (level, message) => SafeInvoke(() => AppendLog(level, message));
        _pollTimer.Tick += async (_, _) => await PollJobAsync();
        AllowDrop = true;
        DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) HandleDroppedFiles(files); };
    }

    public void LoadInitial(string? path)
    {
        if (path is not null && File.Exists(path))
            LoadFile(path);
    }

    public void ApplyLanguage()
    {
        Controls.Clear();
        BuildUi();
        if (_current is null) return;
        _fileInfoEmpty.Visible = false;
        _fileInfoGrid.Visible = true;
    }

    private void SafeInvoke(Action action)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) BeginInvoke(action);
        else action();
    }

    private string T(string key, params object[] args) => Localization.T(_settings, key, args);

    private void BuildUi()
    {

        AutoScroll = true;

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.Bg,
            Padding = Theme.ScalePadding(Theme.Sp3),
            Margin = new Padding(0),
            MinimumSize = Theme.ScaleSize(940, 760),
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        content.Controls.Add(BuildLeftColumn(), 0, 0);
        content.Controls.Add(BuildRightColumn(), 1, 0);
        Controls.Add(content);
    }

    private Control BuildLeftColumn()
    {
        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Theme.Bg,
            Margin = Theme.ScalePadding(0, 0, Theme.Sp2, 0),
        };
        left.RowStyles.Add(Theme.ScaleRow(132));
        left.RowStyles.Add(Theme.ScaleRow(176));
        left.RowStyles.Add(Theme.ScaleRow(128));
        left.RowStyles.Add(Theme.ScaleRow(120));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _dropZone = new DropZone
        {
            Dock = DockStyle.Fill,
            Margin = Theme.ScalePadding(0, 0, 0, Theme.Sp2),
            PrimaryText = T("Dash.DropPrimary"),
            SecondaryText = T("Dash.DropSecondary"),
        };
        _dropZone.FilesDropped += HandleDroppedFiles;
        _dropZone.BrowseRequested += BrowseForFile;
        left.Controls.Add(_dropZone, 0, 0);

        var fileCard = new Card(T("Dash.FileInformation"), Theme.IconFile) { Dock = DockStyle.Fill, Margin = Theme.ScalePadding(0, 0, 0, Theme.Sp2) };
        _fileInfoGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3, BackColor = Theme.Panel, Visible = false };
        _fileInfoGrid.ColumnStyles.Add(Theme.ScaleColumn(84));
        _fileInfoGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _fileInfoGrid.ColumnStyles.Add(Theme.ScaleColumn(92));
        _fileInfoGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < 3; i++) _fileInfoGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));

        _typeBadge = Theme.MakeBadge();
        _encryptionBadge = Theme.MakeBadge();
        _integrityBadge = Theme.MakeBadge();
        _fileNameValue = Theme.MakeValue("—");
        _fileSizeValue = Theme.MakeValue("—");
        _fileModifiedValue = Theme.MakeValue("—");

        _fileInfoGrid.Controls.Add(Theme.MakeLabel(T("Dash.Type")), 0, 0);
        _fileInfoGrid.Controls.Add(_typeBadge, 1, 0);
        _fileInfoGrid.Controls.Add(Theme.MakeLabel(T("Dash.Status")), 2, 0);
        _fileInfoGrid.Controls.Add(_encryptionBadge, 3, 0);
        _fileInfoGrid.Controls.Add(Theme.MakeLabel(T("Dash.Filename")), 0, 1);
        _fileInfoGrid.Controls.Add(_fileNameValue, 1, 1);
        _fileInfoGrid.Controls.Add(Theme.MakeLabel(T("Dash.Integrity")), 2, 1);
        _fileInfoGrid.Controls.Add(_integrityBadge, 3, 1);
        _fileInfoGrid.Controls.Add(Theme.MakeLabel(T("Dash.Size")), 0, 2);
        _fileInfoGrid.Controls.Add(_fileSizeValue, 1, 2);
        _fileInfoGrid.Controls.Add(Theme.MakeLabel(T("Dash.Modified")), 2, 2);
        _fileInfoGrid.Controls.Add(_fileModifiedValue, 3, 2);

        _fileInfoEmpty = new Label
        {
            Text = T("Dash.NoFile"),
            Font = Theme.Body, ForeColor = Theme.TextMuted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        };
        fileCard.Controls.Add(_fileInfoGrid);
        fileCard.Controls.Add(_fileInfoEmpty);
        left.Controls.Add(fileCard, 0, 1);

        var actionsCard = new Card(T("Dash.Actions"), Theme.IconDatabase) { Dock = DockStyle.Fill, Margin = Theme.ScalePadding(0, 0, 0, Theme.Sp2) };
        var actionsGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, BackColor = Theme.Panel };
        for (int i = 0; i < 5; i++) actionsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

        _decryptButton = Theme.MakeFlowButton(T("Dash.Decrypt"));
        _reencryptButton = Theme.MakeFlowButton(T("Dash.ReEncrypt"));
        _mergeButton = Theme.MakeButton(T("Dash.Merge"));
        _exportButton = Theme.MakeButton(T("Dash.Export"));
        _openOutputButton = Theme.MakeButton(T("Dash.Output"));
        _decryptButton.Click += async (_, _) => await DecryptAsync();
        _reencryptButton.Click += async (_, _) => await ReencryptAsync();
        _mergeButton.Click += async (_, _) => await MergeAsync();
        _exportButton.Click += (_, _) => ExportLastOutput();
        _openOutputButton.Click += (_, _) => OpenOutputFolder();
        _tips.SetToolTip(_mergeButton, T("Dash.MergeTip"));

        var buttons = new[] { _decryptButton, _reencryptButton, _mergeButton, _exportButton, _openOutputButton };
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].Dock = DockStyle.Fill;
            buttons[i].Margin = Theme.ScalePadding(0, 4, i < buttons.Length - 1 ? Theme.Sp1 : 0, 4);
            buttons[i].TextAlign = ContentAlignment.MiddleCenter;
            buttons[i].Padding = new Padding(0);
            buttons[i].AutoEllipsis = false;
            actionsGrid.Controls.Add(buttons[i], i, 0);
        }
        _decryptButton.Enabled = false;
        _reencryptButton.Enabled = false;
        _mergeButton.Enabled = false;
        _exportButton.Enabled = false;
        actionsCard.Controls.Add(actionsGrid);
        left.Controls.Add(actionsCard, 0, 2);

        var statusCard = new Card(T("Dash.JobStatus"), Theme.IconHistory) { Dock = DockStyle.Fill, Margin = Theme.ScalePadding(0, 0, 0, Theme.Sp2) };
        var statusGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Theme.Panel };
        statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusGrid.ColumnStyles.Add(Theme.ScaleColumn(56));
        statusGrid.RowStyles.Add(Theme.ScaleRow(26));
        statusGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _stepLabel = Theme.MakeValue(T("Dash.Idle"));
        _stepLabel.ForeColor = Theme.TextMuted;
        _percentLabel = Theme.MakeValue("");
        _percentLabel.TextAlign = ContentAlignment.MiddleRight;
        _percentLabel.ForeColor = Theme.TextMuted;
        _progress = new SlimProgress { Dock = DockStyle.Top, Margin = Theme.ScalePadding(0, Theme.Sp1, 0, 0) };
        statusGrid.Controls.Add(_stepLabel, 0, 0);
        statusGrid.Controls.Add(_percentLabel, 1, 0);
        statusGrid.Controls.Add(_progress, 0, 1);
        statusGrid.SetColumnSpan(_progress, 2);
        statusCard.Controls.Add(statusGrid);
        left.Controls.Add(statusCard, 0, 3);

        var resultsCard = new Card(T("Dash.Results"), Theme.IconCheck) { Dock = DockStyle.Fill, Margin = new Padding(0) };
        _resultsGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, BackColor = Theme.Panel, Visible = false };
        _resultsGrid.ColumnStyles.Add(Theme.ScaleColumn(84));
        _resultsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++) _resultsGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        _resultMode = Theme.MakeValue("—");
        _resultOutput = Theme.MakeValue("—");
        _resultIntegrity = Theme.MakeBadge();
        _resultWarnings = Theme.MakeBadge();
        _resultOutput.Cursor = Cursors.Hand;
        _resultOutput.Click += (_, _) => RevealLastOutput();
        _resultsGrid.Controls.Add(Theme.MakeLabel(T("Dash.Mode")), 0, 0);
        _resultsGrid.Controls.Add(_resultMode, 1, 0);
        _resultsGrid.Controls.Add(Theme.MakeLabel(T("Dash.Output")), 0, 1);
        _resultsGrid.Controls.Add(_resultOutput, 1, 1);
        _resultsGrid.Controls.Add(Theme.MakeLabel(T("Dash.Integrity")), 0, 2);
        _resultsGrid.Controls.Add(_resultIntegrity, 1, 2);
        _resultsGrid.Controls.Add(Theme.MakeLabel(T("Dash.Warnings")), 0, 3);
        _resultsGrid.Controls.Add(_resultWarnings, 1, 3);
        _resultsEmpty = new Label
        {
            Text = T("Dash.NoResults"),
            Font = Theme.Body, ForeColor = Theme.TextMuted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        };
        resultsCard.Controls.Add(_resultsGrid);
        resultsCard.Controls.Add(_resultsEmpty);
        left.Controls.Add(resultsCard, 0, 4);
        return left;
    }

    private Control BuildRightColumn()
    {
        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Bg, Margin = new Padding(0),
        };
        right.RowStyles.Add(Theme.ScaleRow(150));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var recentCard = new Card(T("Dash.RecentFiles"), Theme.IconHistory) { Dock = DockStyle.Fill, Margin = Theme.ScalePadding(0, 0, 0, Theme.Sp2) };
        _recentList = new ListBox
        {
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.TextMain,
            Font = Theme.Body, IntegralHeight = false, ItemHeight = Theme.Scaled(22),
        };
        _recentList.MouseDoubleClick += (_, _) => LoadSelectedRecent();
        recentCard.Controls.Add(_recentList);
        right.Controls.Add(recentCard, 0, 0);
        RefreshRecentList();

        var logCard = new Card(T("Dash.ActivityLog"), Theme.IconContact) { Dock = DockStyle.Fill, Margin = new Padding(0) };
        _logBox = new RichTextBox
        {
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.BgSecondary, ForeColor = Theme.TextMain,
            Font = Theme.Mono, ReadOnly = true, DetectUrls = false,
            Margin = Theme.ScalePadding(Theme.Sp1),
        };
        logCard.Controls.Add(_logBox);
        var clearButton = Theme.MakeToolButton(Theme.IconClear, T("Dash.ClearLog"), _tips);
        clearButton.Click += (_, _) => _logBox.Clear();
        var saveButton = Theme.MakeToolButton(Theme.IconSave, T("Dash.SaveLog"), _tips);
        saveButton.Click += (_, _) => SaveLog();
        var copyButton = Theme.MakeToolButton(Theme.IconCopy, T("Dash.CopyLog"), _tips);
        copyButton.Click += (_, _) => { if (_logBox.TextLength > 0) Clipboard.SetText(_logBox.Text); };
        logCard.AddHeaderControl(clearButton);
        logCard.AddHeaderControl(saveButton);
        logCard.AddHeaderControl(copyButton);
        right.Controls.Add(logCard, 0, 1);
        return right;
    }

    private void BrowseForFile()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Supported files (*.slt;*.sqlite;*.db;*.zip;*.ini;*.cfg;*.config;C_ProfileData*)|*.slt;*.sqlite;*.db;*.zip;*.ini;*.cfg;*.config;C_ProfileData*|All files (*.*)|*.*",
            CheckFileExists = true, Multiselect = true,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            HandleDroppedFiles(dialog.FileNames);
    }

    private void HandleDroppedFiles(string[] files)
    {
        if (_busy) { Logger.Warn("An operation is in progress — wait before loading another file."); return; }
        if (files.Length > 1) Logger.Warn($"{files.Length} files dropped — processing the first (multi-file queue planned).");
        LoadFile(files[0]);
    }

    private void LoadSelectedRecent()
    {
        if (_recentList.SelectedIndex < 0 || _recentList.SelectedIndex >= _settings.RecentFiles.Count) return;
        var path = _settings.RecentFiles[_recentList.SelectedIndex];
        if (!File.Exists(path))
        {
            Logger.Warn("Recent file no longer exists.");
            _settings.RecentFiles.RemoveAt(_recentList.SelectedIndex);
            _settings.Save();
            RefreshRecentList();
            return;
        }
        LoadFile(path);
    }

    private void LoadFile(string path)
    {
        Logger.Info($"Checking file: {Path.GetFileName(path)}");
        SetStep("Checking file…", 20);
        var result = FileDetection.Detect(path);
        SetStep("Checking structure…", 60);

        _current = result;
        _jobId = null;
        _pendingDownload = null;

        _lastOriginalEncryptedPath = null;
        Theme.SetBadge(_typeBadge, result.KindLabel, result.Kind == DetectedKind.Unknown ? Theme.Warning : Theme.Accent);
        Theme.SetBadge(_encryptionBadge, result.Encrypted ? "Encrypted" : "Not encrypted", result.Encrypted ? Theme.Warning : Theme.Success);
        Theme.SetBadge(_integrityBadge, result.IntegrityOk ? "Passed" : "Unknown", result.IntegrityOk ? Theme.Success : Theme.Warning);
        _fileNameValue.Text = result.FileName;
        _fileSizeValue.Text = result.SizeLabel;
        _fileModifiedValue.Text = result.Modified.ToString("yyyy-MM-dd HH:mm");
        _fileInfoEmpty.Visible = false;
        _fileInfoGrid.Visible = true;

        switch (result.Kind)
        {
            case DetectedKind.GameDbEncrypted: Logger.Success("Detected: GameDB (encrypted) — click Decrypt."); break;
            case DetectedKind.GameDbDecrypted:
                Logger.Success("Detected: GameDB (decrypted SQLite).");
                Logger.Info("Edit it, then Re-Encrypt to produce a loadable encrypted .slt.");
                break;
            case DetectedKind.ProfileData:
                Logger.Success("Detected: FH6 C_ProfileData (encrypted) — click Decrypt.");
                break;
            case DetectedKind.ProfileDecrypted:
                Logger.Success("Detected: FH6 profile plaintext (decrypted) — click Re-Encrypt.");
                break;
            case DetectedKind.Method22Zip:
                Logger.Success("Detected: FH6 Method 22 asset ZIP (encrypted) — click Decrypt.");
                break;
            case DetectedKind.Method22ZipDecrypted:
                Logger.Success("Detected: decrypted Method 22 asset ZIP.");
                Logger.Info("Edit the contents, then Re-Encrypt (you'll pick the original encrypted ZIP).");
                break;
            case DetectedKind.PlainZip:
                Logger.Success("Detected: plain ZIP — no encryption (already readable).");
                Logger.Info("This FH6 asset ZIP has no Method 22 (encrypted) entries; open it with any ZIP tool.");
                break;
            case DetectedKind.ConfigFileEncrypted:
                Logger.Success("Detected: FH6 encrypted config file (e.g. PhysicsSettings.ini) — click Decrypt.");
                break;
            case DetectedKind.ConfigFileDecrypted:
                Logger.Success("Detected: decrypted FH6 config file.");
                Logger.Info("Edit it, then Re-Encrypt (you'll pick the original encrypted config).");
                break;
            default: Logger.Warn("Could not identify this file."); break;
        }

        _decryptButton.Enabled = result.Kind is DetectedKind.GameDbEncrypted or DetectedKind.ProfileData or DetectedKind.Method22Zip or DetectedKind.ConfigFileEncrypted;
        _reencryptButton.Enabled = result.Kind is DetectedKind.ProfileDecrypted or DetectedKind.Method22ZipDecrypted or DetectedKind.GameDbDecrypted or DetectedKind.ConfigFileDecrypted;
        _mergeButton.Enabled = result.Kind is DetectedKind.GameDbDecrypted;

        bool anyAction = _decryptButton.Enabled || _reencryptButton.Enabled || _mergeButton.Enabled;
        SetStep(anyAction
            ? (_decryptButton.Enabled ? "Encrypted FH6 file — click Decrypt." : "Decrypted file — click Re-Encrypt.")
            : "File loaded — no FH6 action for this type.", anyAction ? 100 : 0);

        _settings.AddRecentFile(path);
        RefreshRecentList();
    }

    private void RefreshRecentList()
    {
        _recentList.Items.Clear();
        foreach (var path in _settings.RecentFiles)
        {
            var dir = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
            _recentList.Items.Add($"{Path.GetFileName(path)}   ({dir})");
        }
        if (_recentList.Items.Count == 0) _recentList.Items.Add(T("Dash.NoRecent"));
    }

    private async Task DecryptAsync()
    {
        if (_current is null) return;
        if (_current.Kind == DetectedKind.ProfileData)
        {
            await RunOperationAsync(_decryptButton, "Decrypting…", DecryptProfileWithIvsAsync);
            return;
        }
        if (_current.Kind == DetectedKind.Method22Zip)
        {
            await RunOperationAsync(_decryptButton, "Decrypting…", DecryptMethod22ViaBackendAsync, completesViaPolling: true);
            return;
        }
        if (_current.Kind == DetectedKind.ConfigFileEncrypted)
        {
            await RunOperationAsync(_decryptButton, "Decrypting…", DecryptConfigFileAsync);
            return;
        }
        await RunOperationAsync(_decryptButton, "Decrypting…", async () =>
        {
            SetStep("Reading file…", 8);
            Logger.Info("Decryption started.");
            SetStep("Uploading to backend…", 25);

            _lastOriginalEncryptedPath = _current.Path;

            var gamedbIvs = _current.Kind == DetectedKind.GameDbEncrypted ? FindGameDbIvTable() : null;

            using var first = await _backend.UploadAsync(_current.Path, _current.FileName, gamedbIvs);
            var firstStatus = BackendClient.FindString(first.RootElement, "status", "state")?.ToLowerInvariant();
            _jobId = BackendClient.FindString(first.RootElement, "job_id", "jobId", "id");

            if (firstStatus is "failed" or "error")
            {

                Logger.Detail("IV-table decrypt didn't apply (looks like a modified DB) — retrying table-free…");
                SetStep("Retrying without IV table…", 35);
                using var second = await _backend.UploadAsync(_current.Path, _current.FileName, null);
                _jobId = BackendClient.FindString(second.RootElement, "job_id", "jobId", "id");
                var secondStatus = BackendClient.FindString(second.RootElement, "status", "state")?.ToLowerInvariant();
                if (secondStatus is "failed" or "error")
                    throw new InvalidOperationException(BackendClient.FindError(second.RootElement) ?? "backend reported failure");
            }

            if (string.IsNullOrWhiteSpace(_jobId))
                throw new InvalidOperationException("Backend response did not include a job_id.");
            Logger.Detail($"Backend job created: {_jobId}");
            SetStep("Waiting for backend…", 45);
            _pollProgress = 45;
            _pendingDownload = "decrypted";
            _pollTimer.Start();
        }, completesViaPolling: true);
    }

    private static int TryGetM22Count(JsonElement job, string field)
    {
        foreach (var summary in EnumerateSummaries(job))
            if (summary.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Number)
                return v.GetInt32();
        return -1;

        static IEnumerable<JsonElement> EnumerateSummaries(JsonElement job)
        {
            if (job.ValueKind == JsonValueKind.Object)
            {
                if (job.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.Object) yield return s;
                if (job.TryGetProperty("method22", out var m) && m.ValueKind == JsonValueKind.Object
                    && m.TryGetProperty("summary", out var ms) && ms.ValueKind == JsonValueKind.Object) yield return ms;
            }
        }
    }

    private static string? FindIvTable(string fileName)
    {
        foreach (var dir in new[] { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "tools") })
        {
            var p = Path.Combine(dir, fileName);
            if (File.Exists(p)) return p;
        }
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var s = asm.GetManifestResourceStream($"ForzaCryptoTool.Tools.{fileName}");
            if (s is null) return null;
            var dest = Path.Combine(Path.GetTempPath(), "ForzaCryptoTool", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using var fs = File.Create(dest);
            s.CopyTo(fs);
            return dest;
        }
        catch { return null; }
    }

    private static string? FindM22IvTable() => FindIvTable("m22_iv_table.json");
    private static string? FindGameDbIvTable() => FindIvTable("gamedb_iv_table.json");

    private async Task DecryptMethod22ViaBackendAsync()
    {
        SetStep("Uploading asset ZIP to backend…", 25);
        Logger.Info("Method 22 decryption started (backend).");
        var ivTable = FindM22IvTable();
        if (ivTable is not null)
            Logger.Detail($"Including multi-chunk IV table: {Path.GetFileName(ivTable)}");
        using var json = await _backend.DecryptMethod22Async(_current!.Path, _current.FileName, ivTable);
        _jobId = BackendClient.FindString(json.RootElement, "job_id", "jobId", "id");
        if (string.IsNullOrWhiteSpace(_jobId))
            throw new InvalidOperationException("Backend response did not include a job_id.");
        Logger.Detail($"Backend method 22 job created: {_jobId}");
        SetStep("Waiting for backend…", 45);
        _pollProgress = 45;
        _pendingDownload = "method22-decrypted";
        _pollTimer.Start();
    }

    private async Task DecryptProfileWithIvsAsync()
    {

        SetStep("Decrypting profile…", 35);
        Logger.Info("Profile decryption started (IVs derived server-side).");
        var plaintext = await _backend.DecryptProfileWithIvsAsync(_current!.Path, _current.FileName, "");

        SetStep("Saving decrypted profile…", 90);
        var outName = $"{Path.GetFileNameWithoutExtension(_current.FileName)}_decrypted.bin";
        var outPath = UniqueOutputPath(string.IsNullOrEmpty(Path.GetExtension(outName)) ? outName : outName);
        await File.WriteAllBytesAsync(outPath, plaintext);
        _lastOutputPath = outPath;
        SetStep("Finished.", 100);
        Logger.Success($"Profile decrypted → {Path.GetFileName(outPath)} ({plaintext.Length:N0} bytes)");
        ShowResults("Decrypt (profile)", outPath, true, 0);
        SetBusy(false);
        _exportButton.Enabled = true;
        _reencryptButton.Enabled = true;
    }

    private async Task ReencryptProfileWithIvsAsync()
    {

        SetStep("Re-encrypting profile…", 35);
        Logger.Info("Profile re-encryption started (IVs derived server-side).");
        var encrypted = await _backend.ReencryptProfileWithIvsAsync(_current!.Path, _current.FileName, "");

        SetStep("Saving re-encrypted profile…", 90);
        var outName = $"{Path.GetFileNameWithoutExtension(_current.FileName)}_reencrypted";
        var outPath = UniqueOutputPath(outName);
        await File.WriteAllBytesAsync(outPath, encrypted);
        _lastOutputPath = outPath;
        SetStep("Finished.", 100);
        Logger.Success($"Profile re-encrypted → {Path.GetFileName(outPath)} ({encrypted.Length:N0} bytes)");
        Logger.Warn("CLOSE FH6 before copying this over a save so it doesn't autosave over the new file.");
        ShowResults("Re-Encrypt (profile)", outPath, true, 0);
        SetBusy(false);
        _exportButton.Enabled = true;
    }

    private async Task DecryptConfigFileAsync()
    {
        SetStep("Decrypting config…", 35);
        Logger.Info("Config file decryption started (keys-only, no IV capture).");

        _lastOriginalEncryptedPath = _current!.Path;
        var plaintext = await _backend.DecryptConfigFileAsync(_current.Path, _current.FileName);

        SetStep("Saving decrypted config…", 90);

        var ext = Path.GetExtension(_current.FileName);
        var outName = $"{Path.GetFileNameWithoutExtension(_current.FileName)}_decrypted{ext}";
        var outPath = UniqueOutputPath(outName);
        await File.WriteAllBytesAsync(outPath, plaintext);
        _lastOutputPath = outPath;
        SetStep("Finished.", 100);
        Logger.Success($"Config decrypted → {Path.GetFileName(outPath)} ({plaintext.Length:N0} bytes)");
        Logger.Info("NEXT: edit the decrypted config, SAVE, then click Re-Encrypt (you'll pick the original encrypted file).");
        ShowResults("Decrypt (config)", outPath, true, 0);
        SetBusy(false);
        _exportButton.Enabled = true;
        _reencryptButton.Enabled = true;
    }

    private async Task ReencryptConfigFileAsync()
    {

        string? originalPath = _lastOriginalEncryptedPath is not null && File.Exists(_lastOriginalEncryptedPath)
            ? _lastOriginalEncryptedPath : null;
        if (originalPath is null)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Select the ORIGINAL encrypted config this was decrypted from",
                Filter = "FH6 config (*.ini;*.cfg;*.config;*.txt)|*.ini;*.cfg;*.config;*.txt|All files (*.*)|*.*",
                CheckFileExists = true,
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                Logger.Info("Cancelled — no original encrypted config selected.");
                SetBusy(false); SetStep("Cancelled.", 0); return;
            }
            originalPath = dialog.FileName;
        }

        SetStep("Re-encrypting config…", 35);
        Logger.Info("Config file re-encryption started (keys-only).");
        var encrypted = await _backend.ReencryptConfigFileAsync(_current!.Path, _current.FileName, originalPath);

        SetStep("Saving re-encrypted config…", 90);

        var stem = Path.GetFileNameWithoutExtension(_current.FileName)
            .Replace("_decrypted", "").Replace("_edited", "");
        var outName = $"{stem}_reencrypted{Path.GetExtension(_current.FileName)}";
        var outPath = UniqueOutputPath(outName);
        await File.WriteAllBytesAsync(outPath, encrypted);
        _lastOutputPath = outPath;
        SetStep("Finished.", 100);
        Logger.Success($"Config re-encrypted → {Path.GetFileName(outPath)} ({encrypted.Length:N0} bytes)");
        Logger.Warn("CLOSE FH6 before copying this over the game file so it isn't overwritten.");
        ShowResults("Re-Encrypt (config)", outPath, true, 0);
        SetBusy(false);
        _exportButton.Enabled = true;
    }

    private async Task ReencryptMethod22Async()
    {
        string? originalZip;
        using (var dialog = new OpenFileDialog
        {
            Title = "Select the ORIGINAL encrypted Method 22 ZIP this was decrypted from",
            Filter = "FH6 asset ZIP (*.zip)|*.zip|All files (*.*)|*.*",
            CheckFileExists = true,
        })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) { Logger.Info("Cancelled — no original ZIP selected."); SetBusy(false); SetStep("Cancelled.", 0); return; }
            originalZip = dialog.FileName;
        }

        var ivTable = FindM22IvTable();
        SetStep("Uploading edited + original ZIP to backend…", 25);
        Logger.Info("Method 22 re-encryption started (backend).");
        if (ivTable is not null) Logger.Detail($"Including multi-chunk IV table: {Path.GetFileName(ivTable)}");
        using var json = await _backend.ReencryptMethod22Async(_current!.Path, _current.FileName, originalZip, ivTable);
        _jobId = BackendClient.FindString(json.RootElement, "job_id", "jobId", "id");
        if (string.IsNullOrWhiteSpace(_jobId))
            throw new InvalidOperationException("Backend response did not include a job_id.");
        Logger.Detail($"Backend method 22 re-encrypt job created: {_jobId}");
        SetStep("Waiting for backend…", 45);
        _pollProgress = 45;
        _pendingDownload = "method22-reencrypted";
        _pollTimer.Start();
    }

    private async Task ReencryptGameDbDirectAsync()
    {
        var editedPath = _current?.Kind == DetectedKind.GameDbDecrypted && File.Exists(_current.Path)
            ? _current.Path
            : (_lastOutputPath is not null && File.Exists(_lastOutputPath) ? _lastOutputPath : null);
        if (editedPath is null)
        {
            Logger.Warn("No edited GameDB SQLite is loaded.");
            return;
        }

        await RunOperationAsync(_reencryptButton, "Re-Encrypting...", async () =>
        {
            _jobId = null;

            SetStep("Uploading decrypted database...", 35);
            using var json = await _backend.EncryptGameDbAsync(editedPath, Path.GetFileName(editedPath));
            _jobId = BackendClient.FindString(json.RootElement, "job_id", "jobId", "id");
            if (string.IsNullOrWhiteSpace(_jobId))
                throw new InvalidOperationException("Backend response did not include a job_id.");
            Logger.Detail("GameDB encrypt request accepted.");
            SetStep("Waiting for backend...", 45);
            _pollProgress = 45;
            _pendingDownload = "encrypted";
            _pollTimer.Start();
        }, completesViaPolling: true);
    }

    private async Task ReencryptAsync()
    {

        if (_current?.Kind == DetectedKind.ProfileDecrypted)
        {
            await RunOperationAsync(_reencryptButton, "Re-Encrypting…", ReencryptProfileWithIvsAsync);
            return;
        }

        if (_current?.Kind == DetectedKind.Method22ZipDecrypted)
        {
            await RunOperationAsync(_reencryptButton, "Re-Encrypting…", ReencryptMethod22Async, completesViaPolling: true);
            return;
        }
        if (_current?.Kind == DetectedKind.GameDbDecrypted)
        {
            await ReencryptGameDbDirectAsync();
            return;
        }

        if (_current?.Kind == DetectedKind.ConfigFileDecrypted)
        {
            await RunOperationAsync(_reencryptButton, "Re-Encrypting…", ReencryptConfigFileAsync);
            return;
        }
    }

    private async Task MergeAsync()
    {
        if (_current?.Kind != DetectedKind.GameDbDecrypted || !File.Exists(_current.Path))
        {
            Logger.Warn("Load a decrypted GameDB (.sqlite) first, then Merge another one into it.");
            return;
        }
        var basePath = _current.Path;

        string donor;
        using (var pick = new OpenFileDialog
        {
            Title = "Select the DONOR decrypted GameDB (.sqlite) to pull changes FROM",
            Filter = "Decrypted GameDB (*.sqlite;*.db)|*.sqlite;*.db|All files (*.*)|*.*",
            CheckFileExists = true,
            InitialDirectory = Path.GetDirectoryName(basePath),
        })
        {
            if (pick.ShowDialog(this) != DialogResult.OK) return;
            donor = pick.FileName;
        }

        try
        {
            using var fs = File.OpenRead(donor);
            var hdr = new byte[16];
            if (fs.Read(hdr, 0, 16) < 16 || System.Text.Encoding.ASCII.GetString(hdr, 0, 15) != "SQLite format 3")
            {
                Logger.Warn("That donor isn't a decrypted SQLite GameDB. Decrypt it first.");
                return;
            }
        }
        catch (Exception ex) { Logger.Warn($"Could not read donor: {ex.Message}"); return; }

        await RunOperationAsync(_mergeButton, "Merging…", async () =>
        {
            SetStep("Detecting and applying donor changes…", 30);
            var result = await Task.Run(() =>
            {
                var tmp = Path.Combine(Path.GetDirectoryName(basePath)!, "_merge_tmp.sqlite");
                var r = GameDbMerge.MergeChanges(basePath, donor, tmp);
                if (!r.IntegrityOk) { try { File.Delete(tmp); } catch { } return r; }
                var bak = basePath + $".premerge_{DateTime.Now:yyyyMMdd_HHmmss}";
                File.Copy(basePath, bak, overwrite: false);
                File.Copy(tmp, basePath, overwrite: true);
                try { File.Delete(tmp); } catch { }
                PruneBackups(basePath, keep: 2);
                return r;
            });

            if (!result.IntegrityOk)
            {
                SetStep("Merge not applied.", 0);
                Logger.Error($"Merge produced a corrupt DB (integrity: {result.IntegrityResult}); base left unchanged.");
                SetBusy(false);
                return;
            }

            SetStep("Merge applied.", 100);
            Logger.Success($"Merged {result.TotalInserted + result.TotalReplaced} changed row(s) from {Path.GetFileName(donor)} " +
                           $"(+{result.TotalInserted} added, ~{result.TotalReplaced} updated across {result.Tables.Count} table(s)).");
            if (result.ForeignKeyViolations > 0)
                Logger.Warn($"⚠ {result.ForeignKeyViolations} dangling foreign-key reference(s) after merge — usually harmless, verify in-game.");
            Logger.Info("NEXT: click Re-Encrypt to produce a loadable GameDB.");
            ShowResults("Merge", basePath, result.IntegrityOk, result.ForeignKeyViolations);
            SetBusy(false);
            _reencryptButton.Enabled = true;
        });
    }

    private static void PruneBackups(string decryptedPath, int keep)
    {
        try
        {
            var dir = Path.GetDirectoryName(decryptedPath);
            if (string.IsNullOrEmpty(dir)) return;
            foreach (var f in Directory.EnumerateFiles(dir, Path.GetFileName(decryptedPath) + ".premerge_*")
                         .OrderByDescending(File.GetLastWriteTimeUtc).Skip(keep).ToList())
                try { File.Delete(f); } catch { }
        }
        catch {  }
    }

    private void ExportLastOutput()
    {
        if (_lastOutputPath is null || !File.Exists(_lastOutputPath)) { Logger.Warn("Nothing to export yet."); return; }
        using var dialog = new SaveFileDialog { FileName = Path.GetFileName(_lastOutputPath), Filter = "All files (*.*)|*.*", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        File.Copy(_lastOutputPath, dialog.FileName, overwrite: true);
        Logger.Success($"Exported copy: {Path.GetFileName(dialog.FileName)}");
    }

    private void OpenOutputFolder()
    {
        Directory.CreateDirectory(_settings.OutputFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_settings.OutputFolder}\"") { UseShellExecute = true });
    }

    private void RevealLastOutput()
    {
        if (_lastOutputPath is not null && File.Exists(_lastOutputPath))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastOutputPath}\"") { UseShellExecute = true });
    }

    private async Task RunOperationAsync(Button button, string busyText, Func<Task> action, bool completesViaPolling = false)
    {
        var originalText = button.Text;
        SetBusy(true);
        button.Text = busyText;
        try
        {
            await action();
            if (!completesViaPolling) SetBusy(false);
        }
        catch (Exception ex)
        {
            _pollTimer.Stop();
            _pendingDownload = null;
            SetStep("Failed.", 0);
            Logger.Exception("Operation failed", ex);
            SetBusy(false);
        }
        finally { button.Text = originalText; }
    }

    private async Task PollJobAsync()
    {
        if (string.IsNullOrWhiteSpace(_jobId)) { _pollTimer.Stop(); return; }
        try
        {
            using var json = await _backend.GetJobAsync(_jobId);
            var status = BackendClient.FindString(json.RootElement, "status", "state")?.ToLowerInvariant() ?? "unknown";
            _pollProgress = Math.Min(_pollProgress + 4, 90);
            SetStep($"Backend: {status}…", _pollProgress);

            if (status is "failed" or "error")
            {
                _pollTimer.Stop();

                var detail = BackendClient.FindError(json.RootElement) ?? "backend reported failure (no reason returned)";
                throw new InvalidOperationException(detail);
            }

            bool m22Done = _pendingDownload == "method22-decrypted" && status is "decrypted" or "unsupported" or "empty";
            bool m22ReDone = _pendingDownload == "method22-reencrypted" && status is "reencrypted" or "encrypted" or "complete";
            bool terminal = status is "complete" or "completed" or "done" or "finished"
                || (_pendingDownload == "decrypted" && status == "decrypted")
                || (_pendingDownload == "profile-decrypted" && status == "decrypted")
                || m22Done || m22ReDone
                || (_pendingDownload == "encrypted" && status is "encrypted" or "reencrypted");
            if (terminal)
            {
                _pollTimer.Stop();
                await FinishJobAsync(json.RootElement, status);
            }
        }
        catch (Exception ex)
        {
            _pollTimer.Stop();
            _pendingDownload = null;
            SetStep("Failed.", 0);
            Logger.Exception("Job polling failed", ex);
            SetBusy(false);
        }
    }

    private async Task FinishJobAsync(JsonElement job, string status = "")
    {
        var kind = _pendingDownload ?? "decrypted";
        _pendingDownload = null;

        if (kind == "method22-decrypted")
        {
            int decryptedCount = TryGetM22Count(job, "decrypted");
            int unsupportedCount = TryGetM22Count(job, "unsupported");
            if (decryptedCount == 0 && (unsupportedCount > 0 || status is "unsupported" or "empty"))
            {
                _pollTimer.Stop();
                SetStep("Finished — nothing to decrypt.", 100);
                var warn = BackendClient.FindString(job, "warning", "warnings", "message", "detail")
                           ?? "No entries decrypted.";
                Logger.Warn($"Method 22: 0 entries decrypted — {warn}");
                ShowResults("Decrypt", "(no output — no entries decrypted)", null, 1, warn);
                SetBusy(false);
                _reencryptButton.Enabled = false;
                return;
            }
        }

        SetStep("Validating…", 92);
        var validation = BackendClient.FindString(job, "validation", "validation_result", "validationResult", "integrity_check");
        bool integrityOk = validation is null || validation.Contains("pass", StringComparison.OrdinalIgnoreCase) || validation.Contains("ok", StringComparison.OrdinalIgnoreCase);
        SetStep("Downloading result…", 95);
        var baseName = Path.GetFileNameWithoutExtension(_current?.FileName ?? "gamedbRC");
        var outputName = kind switch
        {
            "decrypted" => $"{baseName}_decrypted.sqlite",
            "profile-decrypted" => $"{baseName}_decrypted.bin",
            "method22-decrypted" => $"{baseName}_decrypted.zip",
            "method22-reencrypted" => $"{baseName}_reencrypted.zip",
            "profile-encrypted" => $"{baseName}_reencrypted",
            _ => $"{baseName}_reencrypted.slt",
        };
        var outputPath = UniqueOutputPath(outputName);
        await _backend.DownloadAsync(_jobId!, kind, outputPath);
        _lastOutputPath = outputPath;
        SetStep("Finished.", 100);
        bool isDecrypt = kind is "decrypted" or "profile-decrypted" or "method22-decrypted";
        Logger.Success($"{(isDecrypt ? "Decryption" : "Re-encryption")} complete -> {Path.GetFileName(outputPath)}");

        if (kind is "method22-decrypted" or "method22-reencrypted")
        {
            int done = TryGetM22Count(job, kind == "method22-reencrypted" ? "reencrypted" : "decrypted");
            int skipped = TryGetM22Count(job, "unsupported");
            if (done >= 0)
                Logger.Info($"Method 22: {done} entries {(kind == "method22-reencrypted" ? "re-encrypted" : "decrypted")}"
                            + (skipped > 0 ? $", {skipped} skipped (unsupported)" : ""));
        }
        if (!integrityOk) Logger.Warn($"Validation: {validation}");
        LogNextStep(kind, outputPath);
        ShowResults(isDecrypt ? "Decrypt" : "Re-Encrypt", outputPath, integrityOk, integrityOk ? 0 : 1, validation);
        SetBusy(false);
        _exportButton.Enabled = true;

        _reencryptButton.Enabled = kind is not ("method22-decrypted" or "method22-reencrypted");
    }

    private static void LogNextStep(string kind, string outputPath)
    {
        switch (kind)
        {
            case "decrypted":
                Logger.Info("NEXT: open the decrypted .sqlite, make your edits, SAVE, then click Re-Encrypt.");
                break;
            case "encrypted":
            case "reencrypted":
                Logger.Success("Re-encryption complete — your loadable .slt is ready.");
                break;
            case "method22-decrypted":
                Logger.Info("NEXT: edit the XML inside the decrypted ZIP, then Re-Encrypt (you'll pick the original encrypted ZIP).");
                break;
            case "method22-reencrypted":
                Logger.Success("Re-encryption complete — your re-encrypted asset ZIP is ready.");
                break;
            case "profile-decrypted":
                Logger.Info("NEXT: edit the decrypted profile, then Re-Encrypt.");
                break;
        }
    }

    private void ShowResults(string mode, string outputPath, bool? integrityOk, int warnings, string? warningText = null)
    {
        _resultMode.Text = mode;
        _resultOutput.Text = outputPath;
        if (integrityOk is null) Theme.SetBadge(_resultIntegrity, "Unknown", Theme.Warning);
        else Theme.SetBadge(_resultIntegrity, integrityOk.Value ? "Passed" : "Check failed", integrityOk.Value ? Theme.Success : Theme.Error);
        Theme.SetBadge(_resultWarnings, warnings == 0 ? "0" : $"{warnings} — {warningText}", warnings == 0 ? Theme.Success : Theme.Warning);
        _resultsEmpty.Visible = false;
        _resultsGrid.Visible = true;
    }

    private string UniqueOutputPath(string fileName)
    {
        Directory.CreateDirectory(_settings.OutputFolder);
        var path = Path.Combine(_settings.OutputFolder, fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        int n = 1;
        while (File.Exists(path)) path = Path.Combine(_settings.OutputFolder, $"{stem} ({n++}){ext}");
        return path;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _decryptButton.Enabled = !busy && _current?.Kind is DetectedKind.GameDbEncrypted or DetectedKind.ProfileData or DetectedKind.Method22Zip or DetectedKind.ConfigFileEncrypted;
        _reencryptButton.Enabled = !busy && (!string.IsNullOrWhiteSpace(_jobId) || _current?.Kind is DetectedKind.ProfileDecrypted or DetectedKind.Method22ZipDecrypted or DetectedKind.GameDbDecrypted or DetectedKind.ConfigFileDecrypted);
        _exportButton.Enabled = !busy && _lastOutputPath is not null;
        _openOutputButton.Enabled = !busy;
    }

    private void SetStep(string text, int percent)
    {
        _stepLabel.Text = text;
        _stepLabel.ForeColor = text.StartsWith("Failed") ? Theme.Error : Theme.TextMain;
        _percentLabel.Text = percent > 0 ? $"{percent}%" : "";
        _progress.Value = percent;
    }

    private void AppendLog(LogLevel level, string message)
    {
        var color = level switch
        {
            LogLevel.Success => Theme.Success,
            LogLevel.Warning => Theme.Warning,
            LogLevel.Error => Theme.Error,
            LogLevel.Detail => Theme.TextMuted,
            _ => Theme.TextMain,
        };
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.SelectionColor = Theme.TextMuted;
        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] ");
        _logBox.SelectionColor = color;
        _logBox.AppendText(message + Environment.NewLine);
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    private void SaveLog()
    {
        if (_logBox.TextLength == 0) return;
        using var dialog = new SaveFileDialog
        {
            FileName = $"ForzaCryptoTool_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            File.WriteAllText(dialog.FileName, _logBox.Text);
            Logger.Detail($"Log saved: {Path.GetFileName(dialog.FileName)}");
        }
    }
}
