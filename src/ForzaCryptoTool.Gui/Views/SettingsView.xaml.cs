using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ForzaCryptoTool;

public partial class SettingsView : UserControl
{
    private readonly MainWindow _shell;
    private bool _loading = true;

    internal SettingsView(MainWindow shell)
    {
        InitializeComponent();
        _shell = shell;

        var s = App.Settings;
        OutputBox.Text = s.ResolveOutputFolder();
        UpdateCheck.IsChecked = s.CheckForUpdates;

        EndpointText.Text = App.Backend.MaskedEndpoint;
        ApiKeyText.Text = SecureConfig.MaskApiKey();
        LogPathText.Text = Logger.LogDirectory;

        OutputBox.LostFocus += (_, _) => SaveOutputFolder();
        _loading = false;
    }

    private void OnSettingToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = App.Settings;
        s.CheckForUpdates = UpdateCheck.IsChecked == true;
        s.Save();
        _shell.SetStatus("Settings saved.");
    }

    private void SaveOutputFolder()
    {
        if (_loading) return;
        var folder = OutputBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(folder)) return;

        App.Settings.OutputFolder = folder;
        App.Settings.Save();
        _shell.SetStatus("Output folder saved.");
    }

    private void OnPickOutputClick(object sender, RoutedEventArgs e)
    {

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose where output files go",
            InitialDirectory = App.Settings.ResolveOutputFolder(),
        };
        if (dialog.ShowDialog() != true) return;

        OutputBox.Text = dialog.FolderName;
        SaveOutputFolder();
    }

    private async void OnTestConnectionClick(object sender, RoutedEventArgs e)
    {
        HealthText.Text = "checking…";
        HealthText.Foreground = (Brush)FindResource("TextSecondary");
        _shell.SetBusy(true, "Testing the connection…");

        try
        {
            var (ok, ms) = await App.Backend.CheckHealthAsync();
            HealthText.Text = ok ? $"online · {ms} ms" : "offline";
            HealthText.Foreground = (Brush)FindResource(ok ? "Success" : "Danger");
            _shell.SetStatus(ok ? "Service is reachable." : "Service is not reachable.");
            EndpointText.Text = App.Backend.MaskedEndpoint;
            await _shell.CheckBackendAsync();
        }
        catch (Exception ex)
        {
            Logger.Exception("Health check failed", ex);
            HealthText.Text = "offline";
            HealthText.Foreground = (Brush)FindResource("Danger");
            _shell.SetStatus("Service is not reachable.");
        }
        finally
        {
            _shell.SetBusy(false);
        }
    }

    private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e) =>
        await _shell.CheckForUpdatesAsync(interactive: true);

    private void OnOpenLogsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Logger.LogDirectory);
            Process.Start(new ProcessStartInfo(Logger.LogDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Open logs", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
