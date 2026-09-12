using System.Runtime.InteropServices;
using System.Windows;

namespace ForzaCryptoTool;

internal static class EntryPoint
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 0)
            return RunGui(null);

        if (args.Any(a => a.Equals("--gui", StringComparison.OrdinalIgnoreCase)))
            return RunGui(args.FirstOrDefault(a => !a.StartsWith('-') && File.Exists(a)));

        if (args.Length == 1 && !args[0].StartsWith('-') && File.Exists(args[0]))
            return RunGui(args[0]);

        return RunHeadless(args);
    }

    private static int RunGui(string? initialFile)
    {
        var app = new App { InitialFile = initialFile };
        app.InitializeComponent();
        return app.Run();
    }

    private static int RunHeadless(string[] args)
    {
        WindowsConsole.Attach();
        Logger.Initialize();
        try
        {
            return CliRunner.RunAsync(args).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Logger.Exception("Command failed", ex);
            ConsoleHost.Error(BuildConfig.ShowFullExceptions ? ex.ToString() : ex.Message);
            return ExitCodes.Failure;
        }
        finally
        {
            Logger.Shutdown();
            WindowsConsole.Detach();
        }
    }
}

internal static class WindowsConsole
{
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AttachConsole(int processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AllocConsole();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FreeConsole();

    private const int AttachParentProcess = -1;

    private static bool _attached;
    private static bool _allocated;

    public static void Attach()
    {
        if (_attached) return;

        if (!AttachConsole(AttachParentProcess))
            _allocated = AllocConsole();
        _attached = true;

        try
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }
        catch
        {
        }
    }

    public static void Detach()
    {
        if (_allocated)
        {
            Console.WriteLine();
            Console.Write("Press any key to close...");
            try { Console.ReadKey(true); } catch { }
        }
        if (_attached)
        {
            try { FreeConsole(); } catch { }
            _attached = false;
        }
    }
}
