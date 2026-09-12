using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace ForzaCryptoTool;

public partial class SaveSwapView : UserControl
{
    private readonly MainWindow _shell;
    private readonly SaveSwapService _swap;

    private readonly List<SaveLocator.Candidate> _saves = new();
    private readonly Dictionary<ulong, string> _accountLabels = new();
    private string? _targetPath;
    private string? _donorPath;
    private bool _targetIsRune;

    internal SaveSwapView(MainWindow shell)
    {
        InitializeComponent();
        _shell = shell;
        _swap = new SaveSwapService(App.Backend);
        _swap.Progress += m => Dispatcher.Invoke(() => _shell.SetStatus(m));

        Loaded += (_, _) => Rescan();
    }

    private void Rescan()
    {
        _saves.Clear();
        SaveList.Items.Clear();

        try
        {
            _saves.AddRange(SaveLocator.FindCandidates());
        }
        catch (Exception ex)
        {
            Logger.Exception("Save scan failed", ex);
        }

        foreach (var save in _saves)
            SaveList.Items.Add(BuildSaveRow(save));

        NoSavesText.Visibility = _saves.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_saves.Count > 0)
            SaveList.SelectedIndex = 0;
        else
            _shell.SetStatus("No Forza Horizon 6 save found automatically.");
    }

    private UIElement BuildSaveRow(SaveLocator.Candidate save)
    {
        var panel = new StackPanel();

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock
        {
            Text = save.Source,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = (Brush)FindResource("TextPrimary"),
        });
        header.Children.Add(new TextBlock
        {
            Text = $"· {save.SizeLabel} · {save.Modified:yyyy-MM-dd HH:mm}",
            FontSize = 11.5,
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = (Brush)FindResource("TextSecondary"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(header);

        if (save.Xuid is not null)
        {
            string account = ulong.TryParse(save.Xuid, out ulong parsed) && _accountLabels.TryGetValue(parsed, out string? gamertag)
                ? $"{gamertag}  ·  XUID {save.Xuid}"
                : save.Flavour == SaveLocator.SaveFlavour.Rune
                    ? $"RUNE account  ·  XUID {save.Xuid}"
                    : $"XUID {save.Xuid}";
            panel.Children.Add(new TextBlock
            {
                Text = account,
                FontFamily = (FontFamily)FindResource("MonoFont"),
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = (Brush)FindResource("TextSecondary"),
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = save.Path,
            FontFamily = (FontFamily)FindResource("MonoFont"),
            FontSize = 10.5,
            Margin = new Thickness(0, 3, 0, 0),
            Opacity = 0.65,
            Foreground = (Brush)FindResource("TextSecondary"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        });

        return panel;
    }

    private void OnSaveSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SaveList.SelectedIndex < 0 || SaveList.SelectedIndex >= _saves.Count) return;
        var save = _saves[SaveList.SelectedIndex];
        SetTarget(save.Path, save.Xuid);
    }

    private void SetTarget(string path, string? knownXuid)
    {
        _targetPath = path;
        _targetIsRune = RuneProfile.IsRunePath(path);

        RuneBanner.Visibility = _targetIsRune ? Visibility.Visible : Visibility.Collapsed;

        if (_targetIsRune)
        {
            XuidBox.Text = RuneProfile.XuidDecimal;
            XuidBox.IsEnabled = false;
            GrabXuidButton.IsEnabled = false;
            XuidHint.Text = $"Fixed for RUNE — {RuneProfile.XuidDecimal}";
        }
        else
        {
            XuidBox.IsEnabled = true;
            GrabXuidButton.IsEnabled = true;
            XuidHint.Text = "Your XUID.";

            if (!string.IsNullOrWhiteSpace(knownXuid) && string.IsNullOrWhiteSpace(XuidBox.Text))
                XuidBox.Text = knownXuid;
        }

        RestoreButton.IsEnabled = _swap.HasBackup(path);
        UpdateSwapEnabled();
    }

    private void OnRescanClick(object sender, RoutedEventArgs e) => Rescan();

    private async void OnIdentifyAccountsClick(object sender, RoutedEventArgs e)
    {
        var xuids = _saves.Select(save => ulong.TryParse(save.Xuid, out ulong xuid) ? xuid : 0).Where(xuid => xuid != 0).ToArray();
        if (xuids.Length == 0)
        {
            MessageBox.Show("No account XUIDs were found in the discovered save paths.", "Identify accounts", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        IdentifyAccountsButton.IsEnabled = false;
        _shell.SetBusy(true, "Identifying Xbox accounts…");
        try
        {
            var labels = await XuidGrabber.ResolveAccountsAsync(xuids, message => _shell.SetStatus(message));
            foreach (var pair in labels) _accountLabels[pair.Key] = pair.Value;
            int selected = SaveList.SelectedIndex;
            SaveList.Items.Clear();
            foreach (var save in _saves) SaveList.Items.Add(BuildSaveRow(save));
            SaveList.SelectedIndex = selected;
            _shell.SetStatus($"Identified {labels.Count} Xbox account{(labels.Count == 1 ? "" : "s")}.");
        }
        catch (Exception ex)
        {
            Logger.Exception("Xbox account labeling failed", ex);
            MessageBox.Show(ex.Message, "Identify accounts", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally { IdentifyAccountsButton.IsEnabled = true; _shell.SetBusy(false); }
    }

    private void OnChooseTargetClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose the save to replace (C_ProfileData)",
            Filter = "Forza save|C_ProfileData;C_ProfileBackup;*ProfileData;*ProfileBackup|All files|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        SaveList.SelectedIndex = -1;
        SetTarget(dialog.FileName, null);
        _shell.SetStatus($"Target set: {dialog.FileName}");
    }

    private void OnChooseDonorClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose the donor save",
            Filter = "Forza save|C_ProfileData;C_ProfileBackup;*ProfileData;*ProfileBackup|All files|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        _donorPath = dialog.FileName;
        DonorBox.Text = _donorPath;
        UpdateSwapEnabled();
    }

    private void OnXuidChanged(object sender, TextChangedEventArgs e) => UpdateSwapEnabled();

    private void UpdateSwapEnabled()
    {
        bool xuidOk = _targetIsRune
            || ForzaProfile.TryParseXuid(XuidBox.Text ?? "", out var x) && x != 0;

        SwapButton.IsEnabled = _targetPath is not null
            && _donorPath is not null
            && File.Exists(_donorPath)
            && xuidOk;
    }

    private async void OnGrabXuidClick(object sender, RoutedEventArgs e)
    {
        GrabXuidButton.IsEnabled = false;
        _shell.SetBusy(true, "Looking for your XUID…");
        try
        {
            var account = await XuidGrabber.GrabAccountAsync(m => _shell.SetStatus(m));
            XuidBox.Text = account.Xuid.ToString();
            _accountLabels[account.Xuid] = account.Gamertag;
            _shell.SetStatus($"Found {account.Gamertag} · XUID {account.Xuid}.");
        }
        catch (Exception ex)
        {
            Logger.Exception("XUID detection failed", ex);
            _shell.SetStatus("Could not detect the XUID.");

            if (!XuidGrabber.IsElevated() && ex.Message.Contains("Administrator", StringComparison.OrdinalIgnoreCase))
            {
                var relaunch = MessageBox.Show(
                    ex.Message + "\n\nRestart Forza Crypto Tool as administrator now?",
                    "Detect XUID", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (relaunch == MessageBoxResult.Yes && XuidGrabber.RelaunchAsAdmin())
                    Application.Current.Shutdown();
            }
            else
            {
                MessageBox.Show(ex.Message, "Detect XUID", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        finally
        {
            _shell.SetBusy(false);
            GrabXuidButton.IsEnabled = !_targetIsRune;
        }
    }

    private async void OnSwapClick(object sender, RoutedEventArgs e)
    {
        if (_targetPath is null || _donorPath is null) return;

        var xuidLabel = _targetIsRune ? $"{RuneProfile.XuidDecimal} (RUNE)" : XuidBox.Text;
        var confirm = MessageBox.Show(
            $"""
            This replaces the save files at:
            {Path.GetDirectoryName(_targetPath)}

            Donor:   {Path.GetFileName(_donorPath)}
            Account: {xuidLabel}

            A backup of every replaced file is written next to it.
            Make sure Forza Horizon 6 is closed.

            Continue?
            """,
            "Confirm save swap", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        SwapButton.IsEnabled = false;
        RestoreButton.IsEnabled = false;
        _shell.SetBusy(true, "Swapping save…");

        try
        {
            var result = await _swap.SwapAsync(_donorPath, _targetPath, _targetIsRune ? null : XuidBox.Text);
            ShowSteps(result);
            _shell.SetStatus(result.Success ? "Save swap complete." : "Save swap failed.");

            if (result.Success)
                MessageBox.Show(result.Message, "Save swap", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show(result.Message, "Save swap failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            Logger.Exception("Save swap failed", ex);
            MessageBox.Show(ex.Message, "Save swap failed", MessageBoxButton.OK, MessageBoxImage.Error);
            _shell.SetStatus("Save swap failed.");
        }
        finally
        {
            _shell.SetBusy(false);
            UpdateSwapEnabled();
            RestoreButton.IsEnabled = _targetPath is not null && _swap.HasBackup(_targetPath);
        }
    }

    private void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        if (_targetPath is null) return;

        var confirm = MessageBox.Show(
            "Put back the save that was there before the last swap?\n\n"
            + "The current save is backed up first, so this is reversible too.",
            "Restore original", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var result = _swap.RestoreOriginal(_targetPath);
        ShowSteps(result);
        MessageBox.Show(result.Message, "Restore",
            MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
        _shell.SetStatus(result.Success ? "Original save restored." : "Restore failed.");
    }

    private void ShowSteps(SaveSwapService.SwapResult result)
    {
        StepList.Items.Clear();
        StepsTitle.Text = result.Success ? "Swap complete" : "Swap failed";
        StepsTitle.Foreground = (Brush)FindResource(result.Success ? "Success" : "Danger");
        StepsSummary.Text = result.Message;

        foreach (var step in result.Steps)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var marker = new TextBlock
            {
                Text = step.Ok ? "✓" : "✕",
                Foreground = (Brush)FindResource(step.Ok ? "Success" : "Danger"),
                FontWeight = FontWeights.Bold,
                Width = 18,
                VerticalAlignment = VerticalAlignment.Top,
            };
            Grid.SetColumn(marker, 0);
            row.Children.Add(marker);

            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = step.Name,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimary"),
                TextWrapping = TextWrapping.Wrap,
            });
            text.Children.Add(new TextBlock
            {
                Text = step.Detail,
                FontFamily = (FontFamily)FindResource("MonoFont"),
                FontSize = 11,
                Foreground = (Brush)FindResource("TextSecondary"),
                TextWrapping = TextWrapping.Wrap,
            });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            StepList.Items.Add(row);
        }

        StepsCard.Visibility = Visibility.Visible;
    }
}
