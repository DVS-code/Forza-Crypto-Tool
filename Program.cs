namespace ForzaCryptoTool;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Logger.Initialize();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Logger.Exception("Unhandled exception", e.ExceptionObject as Exception ?? new Exception("unknown"));
        Application.ThreadException += (_, e) => Logger.Exception("UI thread exception", e.Exception);

        try
        {
            ApplicationConfiguration.Initialize();
            if (args.Contains("--settings-only"))
            {
                using var backend = new BackendClient();
                Application.Run(new SettingsForm(AppSettings.Load(), backend));
                return;
            }
            Application.Run(new MainForm(args.FirstOrDefault(File.Exists)));
        }
        finally
        {
            Logger.Shutdown();
        }
    }
}
