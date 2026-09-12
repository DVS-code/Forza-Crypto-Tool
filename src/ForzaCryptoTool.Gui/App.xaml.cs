using System.Windows;
using System.Windows.Threading;

namespace ForzaCryptoTool;

public partial class App : Application
{
    internal string? InitialFile { get; init; }

    internal static AppSettings Settings { get; private set; } = new();
    internal static BackendClient Backend { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Logger.Initialize();
        Logger.Info($"Forza Crypto Tool {BuildConfig.VersionLabel} starting.");

        Settings = AppSettings.Load();
        Backend = new BackendClient();

        DispatcherUnhandledException += OnUnhandledException;

        var window = new MainWindow(InitialFile);
        MainWindow = window;
        window.Show();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Exception("Unhandled exception", e.Exception);
        var detail = BuildConfig.ShowFullExceptions ? e.Exception.ToString() : e.Exception.Message;
        MessageBox.Show(
            $"Something went wrong:\n\n{detail}\n\nA log was written to:\n{Logger.LogDirectory}",
            "Forza Crypto Tool", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Settings.Save();
            Backend?.Dispose();
            Logger.Info("Shutting down.");
        }
        catch {  }
        base.OnExit(e);
    }
}
