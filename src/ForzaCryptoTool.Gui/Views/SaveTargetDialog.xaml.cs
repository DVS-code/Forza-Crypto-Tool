using System.Windows;

namespace ForzaCryptoTool;

internal enum SaveTarget
{
    Cancel,
    OutputFolder,
    OverwriteOriginal,
}

public partial class SaveTargetDialog : Window
{
    internal SaveTarget Result { get; private set; } = SaveTarget.Cancel;

    internal SaveTargetDialog(string subject, string outputPath, string originalPath, bool canOverwrite)
    {
        InitializeComponent();

        SubjectText.Text = subject;
        OutputPathText.Text = outputPath;
        OriginalPathText.Text = originalPath;

        OverwriteOption.IsEnabled = canOverwrite;
        if (!canOverwrite)
            OriginalPathText.Text = "Not available for this file.";

        OverwriteOption.Checked += (_, _) => OverwriteWarning.Visibility = Visibility.Visible;
        OverwriteOption.Unchecked += (_, _) => OverwriteWarning.Visibility = Visibility.Collapsed;

        Loaded += (_, _) => SaveButton.Focus();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Result = OverwriteOption.IsChecked == true ? SaveTarget.OverwriteOriginal : SaveTarget.OutputFolder;

        if (Result == SaveTarget.OverwriteOriginal)
        {
            var answer = MessageBox.Show(
                $"Overwrite this file in the game install?\n\n{OriginalPathText.Text}\n\n"
                + "A timestamped .bak copy is written alongside it first.",
                "Confirm overwrite", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Result = SaveTarget.Cancel;
        DialogResult = false;
        Close();
    }
}
