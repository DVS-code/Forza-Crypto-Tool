namespace ForzaCryptoTool;

internal static class ExitCodes
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int BadUsage = 2;
    public const int FileNotFound = 3;
    public const int BackendOffline = 4;
    public const int Unsupported = 5;
}

internal static class ConsoleHost
{
    public static void Write(string text) => Console.Out.WriteLine(text);

    public static void Error(string text) =>
        WithColour(ConsoleColor.Red, () => Console.Error.WriteLine($"error: {text}"));

    public static void Warn(string text) =>
        WithColour(ConsoleColor.Yellow, () => Console.Error.WriteLine($"warning: {text}"));

    private static void WithColour(ConsoleColor colour, Action write)
    {

        ConsoleColor? previous = null;
        try
        {
            previous = Console.ForegroundColor;
            Console.ForegroundColor = colour;
        }
        catch { }

        write();

        try
        {
            if (previous is ConsoleColor c) Console.ForegroundColor = c;
        }
        catch { }
    }
}
