using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace ForzaCryptoTool;

public partial class DashboardView : UserControl
{
    private readonly MainWindow _shell;
    private readonly CryptoService _crypto;

    private DetectionResult? _file;
    private string? _lastOutput;

    private string? _originalForReencrypt;

    internal DashboardView(MainWindow shell)
    {
        InitializeComponent();
        _shell = shell;
        _crypto = new CryptoService(App.Backend);
        _crypto.Progress += message => Dispatcher.Invoke(() => AppendLog(message, LogKind.Info));

        DragEnter += OnDragEnter;
        DragLeave += OnDragLeave;
        Drop += OnDrop;

        AppendLog("Ready. Drop a file to begin.", LogKind.Info);
    }

    internal void LoadFile(string path)
    {
        if (!File.Exists(path))
        {
            AppendLog($"File not found: {path}", LogKind.Error);
            return;
        }

        try
        {
            _file = FileDetection.Detect(path);
            _originalForReencrypt = null;
            _lastOutput = null;
            ResultCard.Visibility = Visibility.Collapsed;

            FileNameText.Text = _file.FileName;
            FilePathText.Text = path;
            KindText.Text = _file.KindLabel;
            SizeText.Text = _file.SizeLabel;
            ModifiedText.Text = _file.Modified.ToString("yyyy-MM-dd HH:mm");
            StateText.Text = _file.Encrypted ? "Encrypted" : "Decrypted";

            bool canDecrypt = CryptoService.CanDecrypt(_file.Kind);
            bool canEncrypt = CryptoService.CanEncrypt(_file.Kind);
            DecryptButton.IsEnabled = canDecrypt;
            EncryptButton.IsEnabled = canEncrypt;

            DecryptButton.Style = (Style)FindResource(canDecrypt ? "PrimaryButton" : "SecondaryButton");
            EncryptButton.Style = (Style)FindResource(canEncrypt && !canDecrypt ? "PrimaryButton" : "SecondaryButton");

            if (!canDecrypt && !canEncrypt)
            {
                ActionHint.Text = "This file isn't a recognised Forza Horizon 6 encrypted format.";
                ActionHint.Visibility = Visibility.Visible;
            }
            else if (canEncrypt && CryptoService.NeedsOriginalToEncrypt(_file.Kind))
            {
                ActionHint.Text = "Re-encrypting this type needs the original encrypted file — you'll be asked for it.";
                ActionHint.Visibility = Visibility.Visible;
            }
            else
            {
                ActionHint.Visibility = Visibility.Collapsed;
            }

            FileCard.Visibility = Visibility.Visible;
            DropTitle.Text = "Drop another file";
            AppendLog($"Loaded {_file.FileName} — {_file.KindLabel}, {_file.SizeLabel}.", LogKind.Success);
            App.Settings.AddRecent(path);
        }
        catch (Exception ex)
        {
            Logger.Exception("Detection failed", ex);
            AppendLog($"Could not read this file: {ex.Message}", LogKind.Error);
        }
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        DropZone.BorderBrush = (Brush)FindResource("Accent");
        DropZone.Background = (Brush)FindResource("Raised");
        DropTitle.Text = "Release to load";
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {

        DropZone.ClearValue(System.Windows.Controls.Border.BorderBrushProperty);
        DropZone.ClearValue(System.Windows.Controls.Border.BackgroundProperty);
        DropTitle.Text = _file is null ? "Drop a file here" : "Drop another file";
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        OnDragLeave(sender, e);

        _pressedInsideDropZone = false;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        if (paths.Length > 1)
            AppendLog($"{paths.Length} files dropped — loading the first one.", LogKind.Warning);
        LoadFile(paths[0]);
    }

    private bool _pressedInsideDropZone;

    private void OnDropZonePressed(object sender, MouseButtonEventArgs e) => _pressedInsideDropZone = true;

    private void OnDropZoneReleased(object sender, MouseButtonEventArgs e)
    {
        if (!_pressedInsideDropZone) return;
        _pressedInsideDropZone = false;
        OnBrowseClick(sender, e);
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a Forza Horizon 6 file",
            Filter = "Forza files|*.slt;*.sqlite;*.zip;*.ini;*.cfg;*.config;*.bin;C_Profile*|"
                   + "GameDB|*.slt;*.sqlite|Archives|*.zip|Config files|*.ini;*.cfg;*.config|All files|*.*",
        };
        if (dialog.ShowDialog() == true)
            LoadFile(dialog.FileName);
    }

    private async void OnDecryptClick(object sender, RoutedEventArgs e)
    {
        if (_file is null) return;

        var output = AskWhereToSave(CryptoService.DefaultOutputName(_file.Kind, _file.FileName));
        if (output is null) return;

        var source = _file.Path;
        await RunAsync("Decrypting", async () =>
        {
            var result = await _crypto.DecryptAsync(source, output);
            if (result.Success)
            {

                _originalForReencrypt = source;
            }
            return result;
        });
    }

    private async void OnEncryptClick(object sender, RoutedEventArgs e)
    {
        if (_file is null) return;

        string? original = _originalForReencrypt;
        if (CryptoService.NeedsOriginalToEncrypt(_file.Kind) && original is null)
        {
            var dialog = new OpenFileDialog
            {
                Title = $"Choose the ORIGINAL encrypted file this came from",
                Filter = "Original encrypted file|*.ini;*.cfg;*.config;*.zip;*.slt|All files|*.*",
            };
            if (dialog.ShowDialog() != true)
            {
                AppendLog("Re-encrypt cancelled — the original encrypted file is required.", LogKind.Warning);
                return;
            }
            original = dialog.FileName;
        }

        var output = AskWhereToSave(CryptoService.DefaultOutputName(_file.Kind, _file.FileName));
        if (output is null) return;

        var source = _file.Path;
        await RunAsync("Re-encrypting", () => _crypto.EncryptAsync(source, output, original));
    }

    private async Task RunAsync(string verb, Func<Task<CryptoService.CryptoResult>> operation)
    {
        SetActionsEnabled(false);
        _shell.SetBusy(true, $"{verb}…");
        AppendLog($"{verb} {_file?.FileName}…", LogKind.Info);

        try
        {
            var result = await operation();
            if (result.Success)
            {
                _lastOutput = result.OutputPath;
                ShowResult(true, "Done", result.Message, result.OutputPath!);
                AppendLog(result.Message, LogKind.Success);
                _shell.SetStatus("Done.");
            }
            else
            {
                ShowResult(false, "That didn't work", result.Message, null);
                AppendLog(result.Message, LogKind.Error);
                _shell.SetStatus("Failed.");
            }
        }
        catch (Exception ex)
        {
            Logger.Exception($"{verb} failed", ex);
            var message = BuildConfig.ShowFullExceptions ? ex.ToString() : ex.Message;
            ShowResult(false, "That didn't work", message, null);
            AppendLog(message, LogKind.Error);
            _shell.SetStatus("Failed.");
        }
        finally
        {
            _shell.SetBusy(false);
            SetActionsEnabled(true);
        }
    }

    private void SetActionsEnabled(bool enabled)
    {
        if (_file is null) return;
        DecryptButton.IsEnabled = enabled && CryptoService.CanDecrypt(_file.Kind);
        EncryptButton.IsEnabled = enabled && CryptoService.CanEncrypt(_file.Kind);
    }

    private void ShowResult(bool ok, string title, string message, string? outputPath)
    {
        ResultTitle.Text = title;
        ResultTitle.Foreground = (Brush)FindResource(ok ? "Success" : "Danger");
        ResultText.Text = outputPath is null ? message : $"{message}\n{outputPath}";
        OpenFolderButton.Visibility = outputPath is null ? Visibility.Collapsed : Visibility.Visible;
        UseResultButton.Visibility = outputPath is null ? Visibility.Collapsed : Visibility.Visible;
        ResultCard.Visibility = Visibility.Visible;
    }

    private string? AskWhereToSave(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save output as",
            FileName = suggestedName,
            InitialDirectory = App.Settings.ResolveOutputFolder(),
            OverwritePrompt = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        if (_lastOutput is null || !File.Exists(_lastOutput)) return;

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastOutput}\"") { UseShellExecute = true });
    }

    private void OnUseResultClick(object sender, RoutedEventArgs e)
    {
        if (_lastOutput is null) return;
        var original = _file?.Path;
        LoadFile(_lastOutput);

        _originalForReencrypt = original;
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _file = null;
        _lastOutput = null;
        _originalForReencrypt = null;
        FileCard.Visibility = Visibility.Collapsed;
        ResultCard.Visibility = Visibility.Collapsed;
        DropTitle.Text = "Drop a file here";
        _shell.SetStatus("Ready");
    }

    private enum LogKind { Info, Success, Warning, Error }

    private void AppendLog(string message, LogKind kind)
    {
        var brushKey = kind switch
        {
            LogKind.Success => "Success",
            LogKind.Warning => "Warning",
            LogKind.Error => "Danger",
            _ => "TextSecondary",
        };

        var row = new TextBlock
        {
            Text = $"{DateTime.Now:HH:mm:ss}  {message}",
            FontFamily = (FontFamily)FindResource("MonoFont"),
            FontSize = 11.5,
            Foreground = (Brush)FindResource(brushKey),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 3),
        };
        LogList.Items.Add(row);

        LogScroller.ScrollToEnd();

        while (LogList.Items.Count > 300)
            LogList.Items.RemoveAt(0);
    }

    private void OnClearLogClick(object sender, RoutedEventArgs e) => LogList.Items.Clear();
}
