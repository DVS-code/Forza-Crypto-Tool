using System.Security.Cryptography;
using System.Text.Json;

namespace ForzaCryptoTool;

internal sealed class IvTableStore
{
    private readonly Dictionary<string, string[]> _byFingerprint = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _byFingerprint.Count;
    public string? LoadedFrom { get; private set; }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        BuildConfig.AppName,
        "m22_iv_table.json");

    public static IvTableStore Load(string? path = null)
    {
        var store = new IvTableStore();
        path ??= DefaultPath;
        if (!File.Exists(path)) return store;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));

            foreach (string property in (string[])["ct_entries", "entries"])
            {
                if (!document.RootElement.TryGetProperty(property, out var map)
                    || map.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var item in map.EnumerateObject())
                {
                    var ivs = ReadIvs(item.Value);
                    if (ivs.Length > 0) store._byFingerprint[item.Name] = ivs;
                }
            }
            store.LoadedFrom = path;
            Logger.Info($"IV table loaded: {store.Count} entries from {path}");
        }
        catch (Exception ex)
        {
            Logger.Warn($"IV table at {path} could not be read: {ex.Message}");
        }
        return store;
    }

    private static string[] ReadIvs(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("ivs", out var nested))
            value = nested;
        if (value.ValueKind != JsonValueKind.Array) return [];
        return value.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToArray();
    }

    public bool Contains(string fingerprint) => _byFingerprint.ContainsKey(fingerprint);

    public string[]? TryGet(string fingerprint)
        => _byFingerprint.TryGetValue(fingerprint, out var ivs) ? ivs : null;

    public static string Fingerprint(ReadOnlySpan<byte> payload)
    {
        if (payload.Length <= M22Archive.EntryHeaderBytes) return string.Empty;

        using var sha = SHA256.Create();
        var pages = new List<byte>(payload.Length);
        int offset = M22Archive.EntryHeaderBytes;
        while (offset < payload.Length)
        {
            int take = Math.Min(M22Archive.PageDataBytes, payload.Length - offset);
            pages.AddRange(payload.Slice(offset, take).ToArray());
            offset += M22Archive.PageStrideBytes;
        }
        return Convert.ToHexString(sha.ComputeHash(pages.ToArray())).ToLowerInvariant();
    }
}
