namespace ForzaCryptoTool;

internal static class BuildConfig
{
    public const string AppName = "ForzaCryptoTool";
    public static readonly Version AppVersion = new(3, 1, 0);

    public const string UpdateRepo = "DVS-code/Forza-Crypto-Tool";

#if DEBUG

    public static readonly bool IsDebug = true;
    public const string Channel = "dev";

    public static readonly bool VerboseLogging = true;

    public static readonly bool ShowFullExceptions = true;

    public static readonly bool AllowCustomEndpoint = true;

    public static readonly bool RevealEndpoint = true;
#else

    public static readonly bool IsDebug = false;
    public const string Channel = "release";

    public static readonly bool VerboseLogging = false;

    public static readonly bool ShowFullExceptions = false;

    public static readonly bool AllowCustomEndpoint = false;

    public static readonly bool RevealEndpoint = false;
#endif

    public static string VersionLabel => $"{AppVersion.Major}.{AppVersion.Minor}.{AppVersion.Build} ({Channel})";
}
