using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace ForzaCryptoTool;

public partial class AboutView : UserControl
{
    private readonly MainWindow _shell;

    internal AboutView(MainWindow shell)
    {
        InitializeComponent();
        _shell = shell;
        VersionText.Text = $"Version {BuildConfig.VersionLabel}";
    }

    private void OnLinkClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url }) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not open link: {ex.Message}");
            _shell.SetStatus("Could not open the link.");
        }
    }
}
