namespace ForzaCryptoTool;

internal static class BuildConfig
{
    public const string AppName = "ForzaCryptoTool";
    public static readonly Version AppVersion = new(2, 9, 0);

    public const string UpdateRepo = "DVS-code/Forza-Crypto-Tool";

#if DEBUG

    public const bool IsDebug = true;
    public const string Channel = "dev";

    public const bool VerboseLogging = true;

    public const bool ShowFullExceptions = true;

    public const bool AllowCustomEndpoint = true;

    public const bool RevealEndpoint = true;
#else

    public const bool IsDebug = false;
    public const string Channel = "release";

    public const bool VerboseLogging = false;

    public const bool ShowFullExceptions = false;

    public const bool AllowCustomEndpoint = false;

    public const bool RevealEndpoint = false;
#endif

    public static string VersionLabel => $"{AppVersion.Major}.{AppVersion.Minor}.{AppVersion.Build} ({Channel})";
}
