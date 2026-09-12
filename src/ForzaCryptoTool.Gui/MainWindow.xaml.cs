using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ForzaCryptoTool;

public partial class MainWindow : Window
{
    private readonly List<(NavButton Button, Func<UserControl> Build)> _nav = new();
    private UserControl? _current;

    internal MainWindow(string? initialFile = null)
    {
        InitializeComponent();

        VersionText.Text = $"v{BuildConfig.AppVersion.Major}.{BuildConfig.AppVersion.Minor}.{BuildConfig.AppVersion.Build}";
        RestoreWindowPlacement();

        SourceInitialized += (_, _) => WindowChrome.ApplyDark(this);

        var dashboard = new DashboardView(this);
        var profileEditor = new ProfileEditorView(this);
        var assetBrowser = new AssetBrowserView(this);
        AddNav("Dashboard", "Decrypt and re-encrypt files", () => dashboard);
        AddNav("Asset Browser", "Browse and view files inside the game install", () => assetBrowser);
        AddNav("Profile Editor", "Edit encrypted or decrypted FH6 ProfileData", () => profileEditor);
        AddNav("Save Swap", "Transfer a save onto your account", () => new SaveSwapView(this));
        AddNav("Settings", "Backend, output folder, logs", () => new SettingsView(this));
        AddNav("About", "Version, credits, links", () => new AboutView(this));

        var startView = Environment.GetEnvironmentVariable("FCT_START_VIEW");
        int startIndex = startView is null
            ? 0
            : Math.Max(0, _nav.FindIndex(n =>
                n.Button.Label.Replace(" ", "").Equals(startView.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)));
        Select(startIndex);

        if (initialFile is not null)
        {
            Loaded += async (_, _) =>
            {
                var kind = File.Exists(initialFile) ? FileDetection.Detect(initialFile).Kind : DetectedKind.Unknown;
                if (kind is DetectedKind.ProfileDecrypted or DetectedKind.ProfileData)
                {
                    SelectByLabel("Profile Editor");
                    await profileEditor.LoadFileAsync(initialFile);
                }
                else dashboard.LoadFile(initialFile);
            };
        }

        Loaded += async (_, _) =>
        {
            await CheckBackendAsync();
            if (App.Settings.CheckForUpdates)
                await CheckForUpdatesAsync(interactive: false);
        };
        Closing += (_, e) =>
        {
            if (!profileEditor.TryClose() || !assetBrowser.TryClose())
            {
                e.Cancel = true;
                return;
            }
            SaveWindowPlacement();
        };
    }

    private void AddNav(string label, string tooltip, Func<UserControl> build)
    {
        var button = new NavButton { Label = label, ToolTip = tooltip };
        int index = _nav.Count;
        button.Click += (_, _) => Select(index);
        NavPanel.Children.Add(button);
        _nav.Add((button, build));
    }

    private void Select(int index)
    {
        for (int i = 0; i < _nav.Count; i++)
            _nav[i].Button.IsActive = i == index;

        _current = _nav[index].Build();
        ViewHost.Content = _current;
    }

    private void SelectByLabel(string label)
    {
        int index = _nav.FindIndex(n => n.Button.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) Select(index);
    }

    internal void SetStatus(string message)
    {
        Dispatcher.Invoke(() => StatusText.Text = message);
    }

    internal void SetBusy(bool busy, string? message = null)
    {
        Dispatcher.Invoke(() =>
        {
            BusyBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            if (message is not null) StatusText.Text = message;
            else if (!busy) StatusText.Text = "Ready";
            Cursor = busy ? Cursors.Wait : Cursors.Arrow;
        });
    }

    internal async Task CheckBackendAsync()
    {
        SetBackendPill("Connecting…", (Brush)FindResource("TextDisabled"));
        try
        {
            var (ok, ms) = await App.Backend.CheckHealthAsync();
            if (ok)
                SetBackendPill($"Connected · {ms} ms", (Brush)FindResource("Success"));
            else
                SetBackendPill("Backend offline", (Brush)FindResource("Danger"));
        }
        catch
        {
            SetBackendPill("Backend offline", (Brush)FindResource("Danger"));
        }
    }

    internal async Task CheckForUpdatesAsync(bool interactive)
    {
        if (interactive) SetBusy(true, "Checking for updates...");
        try
        {
            var result = await UpdateService.CheckAsync();
            if (result.Status == UpdateCheckStatus.UpdateAvailable && result.Info is not null)
            {
                var info = result.Info;
                string notes = info.Notes.Length > 1200 ? info.Notes[..1200] + "..." : info.Notes;
                var answer = MessageBox.Show(
                    $"Forza Crypto Tool {info.Version} is available.\n\n{notes}\n\nDownload and install it now?",
                    "Update available", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (answer != MessageBoxResult.Yes) return;

                SetBusy(true, $"Downloading {info.AssetName}...");
                var progress = new Progress<int>(percent => SetStatus($"Downloading update... {percent}%"));
                if (await UpdateService.DownloadAndApplyAsync(info, progress))
                {
                    Application.Current.Shutdown();
                    return;
                }
                MessageBox.Show("The update could not be installed. You can download it from GitHub Releases.",
                    "Update failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else if (interactive)
            {
                string message = result.Status switch
                {
                    UpdateCheckStatus.UpToDate => "You are running the latest version.",
                    UpdateCheckStatus.NoReleases => "No published update is available.",
                    _ => "GitHub could not be reached. Try again later.",
                };
                MessageBox.Show(message, "Forza Crypto Tool updates", MessageBoxButton.OK,
                    result.Status == UpdateCheckStatus.NetworkError ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
        }
        finally
        {
            if (interactive) SetBusy(false);
        }
    }

    private void SetBackendPill(string text, Brush colour)
    {
        Dispatcher.Invoke(() =>
        {
            BackendText.Text = text;
            BackendDot.Fill = colour;
        });
    }

    private void RestoreWindowPlacement()
    {
        var s = App.Settings;
        if (s.WindowWidth <= 0 || s.WindowHeight <= 0) return;

        Width = Math.Max(MinWidth, Math.Min(s.WindowWidth, SystemParameters.VirtualScreenWidth));
        Height = Math.Max(MinHeight, Math.Min(s.WindowHeight, SystemParameters.VirtualScreenHeight));
        if (s.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void SaveWindowPlacement()
    {
        var s = App.Settings;
        s.WindowMaximized = WindowState == WindowState.Maximized;

        if (WindowState == WindowState.Normal)
        {
            s.WindowWidth = Width;
            s.WindowHeight = Height;
        }
        s.Save();
    }
}
