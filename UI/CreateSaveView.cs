namespace ForzaCryptoTool;

internal sealed class CreateSaveView : UserControl
{
    private readonly BackendClient _backend;
    private readonly IvCaptureService _capture = new();

    private Button _captureButton = null!;
    private Button _adminButton = null!;
    private Label _adminBadge = null!;
    private Label _status = null!;
    private TextBox _outputBox = null!;
    private Button _openFolderButton = null!;
    private Button _decryptButton = null!;
    private ProgressBar _spinner = null!;
    private ListBox _log = null!;

    private string? _ivsJsonPath;
    private string? _outputDir;
    private string? _capturedProfilePath;

    public CreateSaveView(BackendClient backend)
    {
        _backend = backend;
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        BuildUi();
        RefreshAdminState();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Bg, Padding = Theme.ScalePadding(Theme.Sp3),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        root.Controls.Add(BuildLeft(), 0, 0);
        root.Controls.Add(BuildLog(), 1, 0);
        Controls.Add(root);
    }

    private Control BuildLeft()
    {
        var col = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Bg, Margin = Theme.ScalePadding(0, 0, Theme.Sp2, 0),
        };
        col.RowStyles.Add(Theme.ScaleRow(170));
        col.RowStyles.Add(Theme.ScaleRow(340));
        col.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var info = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Mix(Theme.Panel, Theme.Accent, 0.12f), Margin = Theme.ScalePadding(0, 0, 0, Theme.Sp2), Padding = Theme.ScalePadding(Theme.Sp2, Theme.Sp1, Theme.Sp2, Theme.Sp1) };
        info.Paint += (_, e) => { using var p = new Pen(Theme.Accent); e.Graphics.DrawRectangle(p, 0, 0, info.Width - 1, info.Height - 1); };
        var infoLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent };
        infoLayout.ColumnStyles.Add(Theme.ScaleColumn(34));
        infoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var icon = new Label { Text = Theme.IconInfo, Font = new Font("Segoe MDL2 Assets", 16f), ForeColor = Theme.Accent, AutoSize = true, Dock = DockStyle.Top, Margin = Theme.ScalePadding(0, Theme.Sp1, Theme.Sp1, 0), BackColor = Color.Transparent };
        var text = new Label
        {
            Text = "Capture IVs stores the per-chunk IVs your game needs to decrypt and re-encrypt your profile.\n\n"
                 + "Checklist:\n"
                 + "1. Relaunch as admin if requested.\n"
                 + "2. Start capture.\n"
                 + "3. Launch Forza and stop at the first screen.\n"
                 + "4. Let capture finish and verify the snapshot.\n"
                 + "5. Save or reuse the generated session files.\n\n"
                 + "Output: profile_ivs.json plus C_ProfileData.captured when the matching PGS file is found.",
            Font = Theme.Body, ForeColor = Theme.TextMain, BackColor = Color.Transparent,
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
        };
        infoLayout.Controls.Add(icon, 0, 0);
        infoLayout.Controls.Add(text, 1, 0);
        info.Controls.Add(infoLayout);
        col.Controls.Add(info, 0, 0);

        var card = new Card("Capture IVs", Theme.IconShield) { Dock = DockStyle.Fill, Margin = Theme.ScalePadding(0, 0, 0, Theme.Sp2) };
        var inner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Theme.Panel, Padding = Theme.ScalePadding(Theme.Sp2) };
        inner.RowStyles.Add(Theme.ScaleRow(42));
        inner.RowStyles.Add(Theme.ScaleRow(60));
        inner.RowStyles.Add(Theme.ScaleRow(18));
        inner.RowStyles.Add(Theme.ScaleRow(44));
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var adminRow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Panel, WrapContents = false };
        _adminBadge = Theme.MakeBadge();
        Theme.SetBadge(_adminBadge, "Checking…", Theme.TextMuted);
        _adminButton = Theme.MakeButton("Relaunch as Admin", Theme.IconShield);
        _adminButton.Width = Theme.Scaled(210); _adminButton.Height = Theme.Scaled(34); _adminButton.Margin = Theme.ScalePadding(Theme.Sp2, 0, 0, 0);
        _adminButton.Click += (_, _) => RelaunchAsAdmin();
        adminRow.Controls.Add(_adminBadge);
        adminRow.Controls.Add(_adminButton);
        inner.Controls.Add(adminRow, 0, 0);

        _captureButton = Theme.MakeButton("Capture IVs", Theme.IconUnlock, primary: true);
        _captureButton.Dock = DockStyle.Fill; _captureButton.Height = Theme.Scaled(52); _captureButton.Margin = Theme.ScalePadding(0, Theme.Sp1, 0, 0);
        _captureButton.Click += async (_, _) => await CaptureAsync();
        inner.Controls.Add(_captureButton, 0, 1);

        _spinner = new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 0, Visible = false, Height = 6 };
        inner.Controls.Add(_spinner, 0, 2);

        _status = new Label
        {
            Dock = DockStyle.Fill, Font = Theme.Body, ForeColor = Theme.TextMuted, TextAlign = ContentAlignment.MiddleLeft,
            Text = "Ready. Click Capture IVs to begin.",
        };
        inner.Controls.Add(_status, 0, 3);
        card.Controls.Add(inner);
        col.Controls.Add(card, 0, 1);

        var outCard = new Card("Output", Theme.IconDatabase) { Dock = DockStyle.Fill, Margin = new Padding(0) };
        var outInner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Panel, Padding = Theme.ScalePadding(Theme.Sp2) };
        outInner.RowStyles.Add(Theme.ScaleRow(24));
        outInner.RowStyles.Add(Theme.ScaleRow(42));
        outInner.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        outInner.Controls.Add(new Label { Text = "Verified IV table + matching profile snapshot", Font = Theme.Small, ForeColor = Theme.TextMuted, Dock = DockStyle.Fill }, 0, 0);
        _outputBox = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.BgSecondary, ForeColor = Theme.TextMain, Font = Theme.Body, Margin = Theme.ScalePadding(0, 4, 0, 4) };
        outInner.Controls.Add(_outputBox, 0, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Panel, WrapContents = false, AutoSize = true };
        _openFolderButton = Theme.MakeButton("Open Folder", Theme.IconFolder);
        _openFolderButton.Width = Theme.Scaled(160); _openFolderButton.Height = Theme.Scaled(38); _openFolderButton.Enabled = false;
        _openFolderButton.Click += (_, _) => { if (_outputDir is not null) System.Diagnostics.Process.Start("explorer.exe", $"\"{_outputDir}\""); };
        _decryptButton = Theme.MakeButton("Decrypt Profile", Theme.IconUnlock, primary: true);
        _decryptButton.Width = Theme.Scaled(190); _decryptButton.Height = Theme.Scaled(38); _decryptButton.Enabled = false; _decryptButton.Margin = Theme.ScalePadding(Theme.Sp1, 0, 0, 0);
        _decryptButton.Click += async (_, _) => await DecryptCapturedAsync();
        buttons.Controls.Add(_openFolderButton);
        buttons.Controls.Add(_decryptButton);
        outInner.Controls.Add(buttons, 0, 2);
        outCard.Controls.Add(outInner);
        col.Controls.Add(outCard, 0, 2);
        return col;
    }

    private Control BuildLog()
    {
        var card = new Card("Capture Log", Theme.IconHistory) { Dock = DockStyle.Fill, Margin = new Padding(0) };
        _log = new ListBox
        {
            Dock = DockStyle.Fill, BackColor = Theme.Bg, ForeColor = Theme.TextMain, BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 9f), IntegralHeight = false, ItemHeight = 16,
        };
        card.Controls.Add(_log);
        return card;
    }

    private void RefreshAdminState()
    {
        bool admin = IvCaptureService.IsAdministrator();
        Theme.SetBadge(_adminBadge, admin ? "Administrator" : "Not elevated", admin ? Theme.Success : Theme.Warning);
        _adminButton.Visible = !admin;
        bool toolPresent = IvCaptureService.FindToolExe() is not null;
        _captureButton.Enabled = admin && toolPresent;
        if (!toolPresent)
            SetStatus($"{IvCaptureService.ToolExeName} not found next to the app.", Theme.Error);
        else if (!admin)
            SetStatus("Administrator rights are required. Click Relaunch as Admin.", Theme.Warning);
    }

    private void RelaunchAsAdmin()
    {
        var ok = MessageBox.Show(
            "Capturing IVs requires Administrator rights. Relaunch ForzaCryptoTool as Administrator now?\n\n"
            + "(A UAC prompt will appear. This window will close and a new elevated one will open.)",
            "Administrator required", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (ok != DialogResult.Yes) return;
        if (IvCaptureService.RelaunchAsAdmin())
            Application.Exit();
    }

    private async Task CaptureAsync()
    {
        if (!IvCaptureService.IsAdministrator()) { RelaunchAsAdmin(); return; }
        if (IvCaptureService.IsGameRunning())
        {
            var go = MessageBox.Show(
                "Forza Horizon 6 is already running. The IV tool must attach BEFORE the profile finishes loading, "
                + "so the game should be started AFTER you begin capture.\n\nClose FH6 and try again?",
                "FH6 already running", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (go == DialogResult.OK) IvCaptureService.KillGame();
            return;
        }

        MessageBox.Show(
            "Click OK, then LAUNCH Forza Horizon 6 and press Enter on the FIRST screen.\n\n"
            + "The tool will attach and capture your IVs automatically. Keep this window open.",
            "Start the game now", MessageBoxButtons.OK, MessageBoxIcon.Information);

        _captureButton.Enabled = false;
        _spinner.Visible = true; _spinner.MarqueeAnimationSpeed = 30;
        _log.Items.Clear();
        SetStatus("Waiting for Forza Horizon 6… launch it now.", Theme.Warning);

        try
        {
            void Progress(string line) => BeginInvoke(() =>
            {
                _log.Items.Add(line);
                _log.TopIndex = _log.Items.Count - 1;

                if (line.StartsWith("Found game process", StringComparison.OrdinalIgnoreCase))
                    SetStatus("Game found — installing capture module…", Theme.Warning);
                else if (line.StartsWith("Captured", StringComparison.OrdinalIgnoreCase))
                    SetStatus($"Capturing… ({line})", Theme.Warning);
                else if (line.StartsWith("All independent checks passed", StringComparison.OrdinalIgnoreCase))
                    SetStatus("Verifying capture…", Theme.Warning);
            });

            var result = await _capture.CaptureAsync(Progress);
            if (result.Success && result.IvsJsonPath is not null)
            {
                _ivsJsonPath = result.IvsJsonPath;
                _outputDir = result.OutputDir;
                _capturedProfilePath = result.CapturedProfilePath;
                _outputBox.Text = result.IvsJsonPath;
                _openFolderButton.Enabled = true;
                _decryptButton.Enabled = true;
                SetStatus("Success — IV table captured. FH6 was closed.", Theme.Success);
                MessageBox.Show(result.Message + "\n\nYour IV table:\n" + result.IvsJsonPath,
                    "Capture succeeded", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                SetStatus("Capture failed.", Theme.Error);
                MessageBox.Show(result.Message, "Capture failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, Theme.Error);
            Logger.Exception("IV capture failed", ex);
        }
        finally
        {
            _spinner.MarqueeAnimationSpeed = 0; _spinner.Visible = false;
            _captureButton.Enabled = true;
        }
    }

    private async Task DecryptCapturedAsync()
    {
        if (_ivsJsonPath is null || _outputDir is null) return;
        string encryptedPath;
        if (_capturedProfilePath is not null && File.Exists(_capturedProfilePath))
        {
            encryptedPath = _capturedProfilePath;
        }
        else
        {
            using var pick = new OpenFileDialog
            {
                Title = "Select the encrypted C_ProfileData that was loaded when you captured",
                Filter = "C_ProfileData (*.*)|*.*",
            };
            if (pick.ShowDialog(this) != DialogResult.OK) return;
            encryptedPath = pick.FileName;
        }

        var match = ProfileIvTable.MatchesEncrypted(encryptedPath, _ivsJsonPath);
        if (!match.Ok)
        {
            MessageBox.Show(match.Detail, "Profile and IVs do not match", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus(match.Failure == ProfileIvTable.MatchFailure.StaleCapture
                ? "Stale IV capture — capture again before decrypting."
                : match.Detail, Theme.Error);
            Logger.Warn($"Captured-profile preflight rejected ({match.Failure}): {match.Detail}");
            return;
        }

        _decryptButton.Enabled = false;
        SetStatus("Decrypting on backend…", Theme.Warning);
        try
        {
            var plaintext = await _backend.DecryptProfileWithIvsAsync(encryptedPath, Path.GetFileName(encryptedPath), _ivsJsonPath);
            using var save = new SaveFileDialog { Title = "Save decrypted profile", FileName = "C_ProfileData_decrypted.bin", Filter = "Decrypted profile (*.bin)|*.bin|All files (*.*)|*.*" };
            if (save.ShowDialog(this) == DialogResult.OK)
            {
                await File.WriteAllBytesAsync(save.FileName, plaintext);
                SetStatus($"Decrypted {plaintext.Length:N0} bytes → {Path.GetFileName(save.FileName)}", Theme.Success);
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, Theme.Error);
            Logger.Exception("Decrypt captured profile failed", ex);
        }
        finally
        {
            _decryptButton.Enabled = true;
        }
    }

    private void SetStatus(string text, Color color) { _status.Text = text; _status.ForeColor = color; }
}
