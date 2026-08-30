namespace ForzaCryptoTool;

internal static class CliEntryPoint
{
    public static async Task<int> Main(string[] args)
    {
        Logger.Initialize();
        try
        {
            if (args.Length == 0)
            {
                CliRunner.PrintHelp();
                return ExitCodes.BadUsage;
            }
            return await CliRunner.RunAsync(args);
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
        }
    }
}
