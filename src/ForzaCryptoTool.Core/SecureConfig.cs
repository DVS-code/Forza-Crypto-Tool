namespace ForzaCryptoTool;

internal static class SecureConfig
{
    public const string EnvVar = "FCT_BACKEND_URL";
    public const string ApiKeyEnvVar = "FCT_API_KEY";

    public static string ConfigDir
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), BuildConfig.AppName);

            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrWhiteSpace(xdg))
                xdg = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(xdg, "forzacryptotool");
        }
    }

    private static string EndpointFile => Path.Combine(ConfigDir, "endpoint.bin");
    private static string ApiKeyFile => Path.Combine(ConfigDir, "apikey.bin");

    private const string EndpointPurpose = "endpoint";
    private const string ApiKeyPurpose = "apikey";

    public enum Source { Environment, UserConfig, Unconfigured }

    public sealed record Resolved(string Url, Source Source);

    public static Resolved ResolveEndpoint()
    {
        var env = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(env))
            return new Resolved(env.Trim().TrimEnd('/'), Source.Environment);

        var stored = SecretStore.Read(EndpointFile, EndpointPurpose);
        if (!string.IsNullOrWhiteSpace(stored))
            return new Resolved(stored.Trim().TrimEnd('/'), Source.UserConfig);

        return new Resolved(string.Empty, Source.Unconfigured);
    }

    public static void SaveUserEndpoint(string url) =>
        SecretStore.Write(EndpointFile, url.Trim().TrimEnd('/'), EndpointPurpose);

    public static void ClearUserEndpoint()
    {
        try { if (File.Exists(EndpointFile)) File.Delete(EndpointFile); }
        catch {  }
    }

    public static string? ResolveApiKey()
    {
        var env = Environment.GetEnvironmentVariable(ApiKeyEnvVar);
        if (!string.IsNullOrWhiteSpace(env))
            return env.Trim();

        var stored = SecretStore.Read(ApiKeyFile, ApiKeyPurpose);
        if (!string.IsNullOrWhiteSpace(stored))
            return stored.Trim();

        return null;
    }

    public static bool HasApiKey() => !string.IsNullOrWhiteSpace(ResolveApiKey());

    public static void SaveUserApiKey(string key) =>
        SecretStore.Write(ApiKeyFile, key.Trim(), ApiKeyPurpose);

    public static void ClearUserApiKey()
    {
        try { if (File.Exists(ApiKeyFile)) File.Delete(ApiKeyFile); }
        catch {  }
    }

    public static string MaskApiKey()
    {
        var key = ResolveApiKey();
        if (string.IsNullOrEmpty(key))
            return "(not configured)";
        if (BuildConfig.RevealEndpoint && key.Length > 8)
            return $"{key[..4]}…{key[^4..]}";
        return "configured ••••";
    }

    public static string Mask(string url)
    {
        if (BuildConfig.RevealEndpoint)
            return url;
        if (string.IsNullOrWhiteSpace(url))
            return "(not configured)";
        try
        {
            var uri = new Uri(url);
            var host = uri.Host;
            var firstDot = host.IndexOf('.');
            string maskedHost;
            if (firstDot > 2)
            {
                var label = host[..firstDot];
                var keep = Math.Min(2, label.Length);
                maskedHost = label[..keep] + new string('•', Math.Max(3, label.Length - keep)) + host[firstDot..];
            }
            else
            {
                maskedHost = new string('•', 8) + host[host.LastIndexOf('.')..];
            }
            return $"{uri.Scheme}://{maskedHost}";
        }
        catch
        {
            return "(configured)";
        }
    }
}
