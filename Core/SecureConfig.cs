using System.Security.Cryptography;
using System.Text;

namespace ForzaCryptoTool;

internal static class SecureConfig
{
    public const string EnvVar = "FCT_BACKEND_URL";

    private const string DefaultEndpointObfuscated = "";

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), BuildConfig.AppName);

    private static string EndpointFile => Path.Combine(Dir, "endpoint.bin");

    private static readonly byte[] Entropy = SHA256.HashData(Encoding.UTF8.GetBytes("ForzaCryptoTool/dpapi/endpoint"));

    public enum Source { Environment, UserConfig, EmbeddedDefault }

    public sealed record Resolved(string Url, Source Source);

    public static Resolved ResolveEndpoint()
    {
        var env = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(env))
            return new Resolved(env.Trim().TrimEnd('/'), Source.Environment);

        var embedded = string.IsNullOrEmpty(DefaultEndpointObfuscated)
            ? ""
            : Obfuscation.Reveal(DefaultEndpointObfuscated).TrimEnd('/');
        var stored = TryReadUserEndpoint();
        if (!string.IsNullOrWhiteSpace(stored))
        {
            var storedUrl = stored!.Trim().TrimEnd('/');
            if (!IsStaleTryCloudflareOverride(storedUrl, embedded))
                return new Resolved(storedUrl, Source.UserConfig);
            TryReplaceUserEndpoint(embedded);
        }

        return new Resolved(embedded, Source.EmbeddedDefault);
    }

    public static void SaveUserEndpoint(string url)
    {
        Directory.CreateDirectory(Dir);
        var clear = Encoding.UTF8.GetBytes(url.Trim().TrimEnd('/'));
        var encrypted = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(EndpointFile, encrypted);
    }

    public static void ClearUserEndpoint()
    {
        try { if (File.Exists(EndpointFile)) File.Delete(EndpointFile); }
        catch {  }
    }

    public const string ApiKeyEnvVar = "FCT_API_KEY";

    private const string DefaultApiKeyObfuscated = "";

    private static string ApiKeyFile => Path.Combine(Dir, "apikey.bin");
    private static readonly byte[] ApiKeyEntropy = SHA256.HashData(Encoding.UTF8.GetBytes("ForzaCryptoTool/dpapi/apikey"));

    public static string? ResolveApiKey()
    {
        var env = Environment.GetEnvironmentVariable(ApiKeyEnvVar);
        if (!string.IsNullOrWhiteSpace(env))
            return env.Trim();

        var stored = TryReadProtected(ApiKeyFile, ApiKeyEntropy);
        if (!string.IsNullOrWhiteSpace(stored))
            return stored.Trim();

        return string.IsNullOrEmpty(DefaultApiKeyObfuscated) ? null : Obfuscation.Reveal(DefaultApiKeyObfuscated);
    }

    public static bool HasApiKey() => !string.IsNullOrWhiteSpace(ResolveApiKey());

    public static void SaveUserApiKey(string key)
    {
        Directory.CreateDirectory(Dir);
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), ApiKeyEntropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(ApiKeyFile, encrypted);
    }

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

    private static string? TryReadProtected(string file, byte[] entropy)
    {
        try
        {
            if (!File.Exists(file))
                return null;
            var clear = ProtectedData.Unprotect(File.ReadAllBytes(file), entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clear);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadUserEndpoint()
    {
        try
        {
            if (!File.Exists(EndpointFile))
                return null;
            var encrypted = File.ReadAllBytes(EndpointFile);
            var clear = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clear);
        }
        catch
        {

            return null;
        }
    }

    private static bool IsStaleTryCloudflareOverride(string storedUrl, string embeddedUrl)
    {
        if (BuildConfig.AllowCustomEndpoint || string.Equals(storedUrl, embeddedUrl, StringComparison.OrdinalIgnoreCase))
            return false;

        return Uri.TryCreate(storedUrl, UriKind.Absolute, out var stored)
            && Uri.TryCreate(embeddedUrl, UriKind.Absolute, out var embedded)
            && stored.Host.EndsWith(".trycloudflare.com", StringComparison.OrdinalIgnoreCase)
            && embedded.Host.EndsWith(".trycloudflare.com", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryReplaceUserEndpoint(string url)
    {
        try { SaveUserEndpoint(url); }
        catch {  }
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
            string maskedHost;
            var firstDot = host.IndexOf('.');
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
