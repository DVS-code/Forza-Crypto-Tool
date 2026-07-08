using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ForzaCryptoTool;

internal sealed class BackendClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private SecureConfig.Resolved _endpoint;

    private static readonly string[] V26Bases =
    {
    };

    private readonly List<string> _bases = new();

    private string? _activeBase;
    private readonly object _baseLock = new();

    public BackendClient()
    {
        _endpoint = SecureConfig.ResolveEndpoint();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"{BuildConfig.AppName}/{BuildConfig.AppVersion}");
        ApplyAuth();
        BuildBaseList();
        Logger.Info($"Backend endpoint loaded from {_endpoint.Source}.");
    }

    public string MaskedEndpoint => SecureConfig.Mask(_activeBase ?? _endpoint.Url);
    public SecureConfig.Source EndpointSource => _endpoint.Source;
    public bool HasApiKey => SecureConfig.HasApiKey();
    public string MaskedApiKey => SecureConfig.MaskApiKey();

    public void Reload()
    {
        _endpoint = SecureConfig.ResolveEndpoint();
        ApplyAuth();
        BuildBaseList();
        lock (_baseLock) _activeBase = null;
        Logger.Info($"Backend endpoint reloaded from {_endpoint.Source}.");
    }

    private void BuildBaseList()
    {
        _bases.Clear();
        void Add(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            var u = url.Trim().TrimEnd('/');
            if (!_bases.Any(b => string.Equals(b, u, StringComparison.OrdinalIgnoreCase)))
                _bases.Add(u);
        }

        if (_endpoint.Source is SecureConfig.Source.Environment or SecureConfig.Source.UserConfig)
            Add(_endpoint.Url);
        foreach (var b in V26Bases) Add(b);
    }

    private void ApplyAuth()
    {
        var key = SecureConfig.ResolveApiKey();
        _http.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(key)
            ? null
            : new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        if (string.IsNullOrWhiteSpace(key))
            Logger.Warn("No app key configured — protected backend routes will return 401. Set one in Settings or FCT_API_KEY.");
        else
            Logger.Detail("App key loaded for backend auth.");
    }

    private static Uri Build(string baseUrl, string path)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Backend endpoint is not configured.");
        return new Uri($"{baseUrl.TrimEnd('/')}{path}");
    }

    public async Task<string?> SelectBackendAsync()
    {
        lock (_baseLock) { if (_activeBase is not null) return _activeBase; }

        foreach (var b in _bases)
        {
            var (ok, _) = await ProbeHealthAsync(b);
            if (ok)
            {
                lock (_baseLock) _activeBase = b;
                Logger.Info($"Connected backend: {SecureConfig.Mask(b)}");
                return b;
            }
            Logger.Detail($"Backend unhealthy, trying next: {SecureConfig.Mask(b)}");
        }
        Logger.Warn("Backend offline. Please try again later. (No candidate base passed the health check.)");
        return null;
    }

    private async Task<string> RequireActiveBaseAsync()
    {
        var b = await SelectBackendAsync();
        if (b is null)
            throw new InvalidOperationException("Backend offline. Please try again later.");
        return b;
    }

    private async Task<(bool ok, long ms)> ProbeHealthAsync(string baseUrl)
    {
        var watch = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            using var resp = await _http.GetAsync(Build(baseUrl, "/api/health"), cts.Token);
            if (resp.IsSuccessStatusCode) { watch.Stop(); return (true, watch.ElapsedMilliseconds); }
            using var resp2 = await _http.GetAsync(Build(baseUrl, "/health"), cts.Token);
            watch.Stop();
            return (resp2.IsSuccessStatusCode, watch.ElapsedMilliseconds);
        }
        catch
        {
            watch.Stop();
            return (false, watch.ElapsedMilliseconds);
        }
    }

    public async Task<(bool ok, long ms)> CheckHealthAsync()
    {
        var b = await SelectBackendAsync();
        if (b is null) return (false, 0);
        return await ProbeHealthAsync(b);
    }

    private async Task<HttpResponseMessage> SendAsync(Func<string, HttpRequestMessage> make, CancellationToken ct = default)
    {
        var active = await RequireActiveBaseAsync();
        try
        {
            using var req = make(active);
            return await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {

            Logger.Warn($"Backend request failed on {SecureConfig.Mask(active)} ({ex.GetType().Name}); attempting one failover.");
            var next = await FindNextHealthyAsync(active);
            if (next is null)
            {
                Logger.Error("Backend offline. Please try again later. (Failover exhausted.)");
                throw new InvalidOperationException("Backend offline. Please try again later.", ex);
            }
            lock (_baseLock) _activeBase = next;
            Logger.Info($"Connected backend: {SecureConfig.Mask(next)} (after failover)");
            using var req = make(next);
            return await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        }
    }

    private async Task<string?> FindNextHealthyAsync(string failed)
    {
        foreach (var b in _bases)
        {
            if (string.Equals(b, failed, StringComparison.OrdinalIgnoreCase)) continue;
            var (ok, _) = await ProbeHealthAsync(b);
            if (ok) return b;
        }
        return null;
    }

    private HttpRequestMessage Post(string baseUrl, string path, HttpContent content) =>
        new(HttpMethod.Post, Build(baseUrl, path)) { Content = content };
    private HttpRequestMessage Get(string baseUrl, string path) =>
        new(HttpMethod.Get, Build(baseUrl, path));

    public async Task<JsonDocument> UploadAsync(string filePath, string fileName, string? gamedbIvTablePath = null)
    {
        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var ivBytes = gamedbIvTablePath is not null && File.Exists(gamedbIvTablePath)
            ? await File.ReadAllBytesAsync(gamedbIvTablePath) : null;
        var ivName = gamedbIvTablePath is not null ? Path.GetFileName(gamedbIvTablePath) : null;

        using var response = await SendAsync(b =>
        {
            var form = new MultipartFormDataContent();
            AddFile(form, "file", fileBytes, fileName);
            if (ivBytes is not null) AddJson(form, "iv_table", ivBytes, ivName!);
            return Post(b, "/api/jobs/upload", form);
        });
        return await ReadJsonAsync(response);
    }

    public async Task<JsonDocument> DecryptProfileAsync(string filePath, string fileName)
    {
        var fileBytes = await File.ReadAllBytesAsync(filePath);
        using var response = await SendAsync(b =>
        {
            var form = new MultipartFormDataContent();
            AddFile(form, "file", fileBytes, fileName);
            return Post(b, "/api/profile/decrypt", form);
        });
        return await ReadJsonAsync(response);
    }

    public async Task<JsonDocument> DecryptMethod22Async(string filePath, string fileName, string? ivTablePath = null)
    {
        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var ivBytes = ivTablePath is not null && File.Exists(ivTablePath)
            ? await File.ReadAllBytesAsync(ivTablePath) : null;
        var ivName = ivTablePath is not null ? Path.GetFileName(ivTablePath) : null;

        using var response = await SendAsync(b =>
        {
            var form = new MultipartFormDataContent();
            AddFile(form, "file", fileBytes, fileName);
            if (ivBytes is not null) AddJson(form, "iv_table", ivBytes, ivName!);
            return Post(b, "/api/method22/decrypt", form);
        });
        return await ReadJsonAsync(response);
    }

    public async Task<JsonDocument> ReencryptMethod22Async(
        string editedZipPath, string editedFileName, string originalZipPath, string? ivTablePath = null)
    {
        var editedBytes = await File.ReadAllBytesAsync(editedZipPath);
        var origBytes = await File.ReadAllBytesAsync(originalZipPath);
        var origName = Path.GetFileName(originalZipPath);
        var ivBytes = ivTablePath is not null && File.Exists(ivTablePath)
            ? await File.ReadAllBytesAsync(ivTablePath) : null;
        var ivName = ivTablePath is not null ? Path.GetFileName(ivTablePath) : null;

        using var response = await SendAsync(b =>
        {
            var form = new MultipartFormDataContent();
            AddFile(form, "file", editedBytes, editedFileName);
            AddFile(form, "original", origBytes, origName);
            if (ivBytes is not null) AddJson(form, "iv_table", ivBytes, ivName!);
            return Post(b, "/api/method22/reencrypt", form);
        });
        return await ReadJsonAsync(response);
    }

    public async Task<byte[]> DecryptConfigFileAsync(string filePath, string fileName)
    {
        var fileBytes = await File.ReadAllBytesAsync(filePath);
        using var response = await SendAsync(b =>
        {
            var form = new MultipartFormDataContent();
            AddFile(form, "file", fileBytes, fileName);
            return Post(b, "/api/configfile/decrypt", form);
        });
        return await ReadBytesAsync(response);
    }

    public async Task<byte[]> ReencryptConfigFileAsync(
        string editedPlaintextPath, string editedFileName, string originalEncryptedPath)
    {
        var editedBytes = await File.ReadAllBytesAsync(editedPlaintextPath);
        var origBytes = await File.ReadAllBytesAsync(originalEncryptedPath);
        var origName = Path.GetFileName(originalEncryptedPath);
        using var response = await SendAsync(b =>
        {
            var form = new MultipartFormDataContent();
            AddFile(form, "file", editedBytes, editedFileName);
            AddFile(form, "original", origBytes, origName);
            return Post(b, "/api/configfile/reencrypt", form);
        });
        return await ReadBytesAsync(response);
    }

    public async Task<JsonDocument> ForzaTechDecryptAsync(string filePath, string fileName, string game, string keyType)
    {
        var fileBytes = await File.ReadAllBytesAsync(filePath);
        using var response = await SendAsync(b =>
        {
            var form = new MultipartFormDataContent();
            AddFile(form, "file", fileBytes, fileName);
            AddText(form, "game", game);
            AddText(form, "key_type", keyType);
            return Post(b, "/api/forzatech/decrypt", form);
        });
        return await ReadJsonAsync(response);
    }

    public async Task<JsonDocument> ForzaTechReencryptAsync(
        string editedPlaintextPath, string editedFileName, string originalEncryptedPath, string game, string keyType)
    {
        var editedBytes = await File.ReadAllBytesAsync(editedPlaintextPath);
        var origBytes = await File.ReadAllBytesAsync(originalEncryptedPath);
        var origName = Path.GetFileName(originalEncryptedPath);
        using var response = await SendAsync(b =>
        {
            var form = new MultipartFormDataContent();
            AddFile(form, "file", editedBytes, editedFileName);
            AddFile(form, "original", origBytes, origName);
            AddText(form, "game", game);
            AddText(form, "key_type", keyType);
            return Post(b, "/api/forzatech/reencrypt", form);
        });
        return await ReadJsonAsync(response);
    }

    public async Task<byte[]> DecryptProfileWithIvsAsync(string profilePath, string profileFileName, string ivsJsonPath)
    {
        var fileBytes = await File.ReadAllBytesAsync(profilePath);

        var ivBytes = !string.IsNullOrWhiteSpace(ivsJsonPath) && File.Exists(ivsJsonPath)
            ? await File.ReadAllBytesAsync(ivsJsonPath) : null;
        var ivName = ivBytes is not null ? Path.GetFileName(ivsJsonPath) : null;
        using var response = await SendAsync(b =>
        {
            var form = new MultipartFormDataContent();
            AddFile(form, "file", fileBytes, profileFileName);
            if (ivBytes is not null) AddJson(form, "ivs", ivBytes, ivName!);
            return Post(b, "/api/profile/decrypt-ivs", form);
        });
        return await ReadBytesAsync(response);
    }

    public async Task<byte[]> ReencryptProfileWithIvsAsync(string editedPlaintextPath, string editedFileName, string ivsJsonPath)
    {
        var fileBytes = await File.ReadAllBytesAsync(editedPlaintextPath);

        var ivBytes = !string.IsNullOrWhiteSpace(ivsJsonPath) && File.Exists(ivsJsonPath)
            ? await File.ReadAllBytesAsync(ivsJsonPath) : null;
        var ivName = ivBytes is not null ? Path.GetFileName(ivsJsonPath) : null;
        using var response = await SendAsync(b =>
        {
            var form = new MultipartFormDataContent();
            AddFile(form, "file", fileBytes, editedFileName);
            if (ivBytes is not null) AddJson(form, "ivs", ivBytes, ivName!);
            return Post(b, "/api/profile/reencrypt-ivs", form);
        });
        return await ReadBytesAsync(response);
    }

    public async Task<byte[]> SwapProfileXuidWithIvsAsync(
        string profilePath, string profileFileName,
        string donorIvsJsonPath, string? activeProfilePath,
        string targetXuid, string? sourceXuid = null)
    {
        var fileBytes = await File.ReadAllBytesAsync(profilePath);
        var donorIvBytes = !string.IsNullOrWhiteSpace(donorIvsJsonPath) && File.Exists(donorIvsJsonPath)
            ? await File.ReadAllBytesAsync(donorIvsJsonPath) : null;
        var donorIvName = donorIvBytes is not null ? Path.GetFileName(donorIvsJsonPath) : null;
        using var response = await SendAsync(b =>
        {
            var form = new MultipartFormDataContent();
            AddFile(form, "file", fileBytes, profileFileName);
            if (donorIvBytes is not null) AddJson(form, "donor_ivs", donorIvBytes, donorIvName!);

            AddText(form, "target_xuid", targetXuid);
            if (!string.IsNullOrWhiteSpace(sourceXuid)) AddText(form, "source_xuid", sourceXuid);
            AddText(form, "mode", "donor");
            return Post(b, "/api/profile/swap-ivs", form);
        });
        return await ReadBytesAsync(response);
    }

    public async Task<byte[]> SwapProfileAsync(string donorPath, string donorFileName, string targetXuid, string? sourceXuid = null, string? profileKind = null)
    {
        var fileBytes = await File.ReadAllBytesAsync(donorPath);
        using var response = await SendAsync(b =>
        {
            var form = new MultipartFormDataContent();
            AddFile(form, "file", fileBytes, donorFileName);
            AddText(form, "target_xuid", targetXuid);
            if (!string.IsNullOrWhiteSpace(sourceXuid)) AddText(form, "source_xuid", sourceXuid);
            if (!string.IsNullOrWhiteSpace(profileKind)) AddText(form, "profile_kind", profileKind);
            return Post(b, "/api/profile/swap", form);
        });
        return await ReadBytesAsync(response);
    }

    public async Task<JsonDocument> GetJobAsync(string jobId)
    {
        using var response = await SendAsync(b => Get(b, $"/api/jobs/{Uri.EscapeDataString(jobId)}"));
        return await ReadJsonAsync(response);
    }

    public async Task<JsonDocument> ReencryptAsync(string jobId, string? modifiedFilePath, string? modifiedFileName, string? gamedbIvTablePath = null)
    {
        var hasFile = modifiedFilePath is not null && File.Exists(modifiedFilePath);
        var fileBytes = hasFile ? await File.ReadAllBytesAsync(modifiedFilePath!) : null;
        var ivBytes = gamedbIvTablePath is not null && File.Exists(gamedbIvTablePath)
            ? await File.ReadAllBytesAsync(gamedbIvTablePath) : null;
        var ivName = gamedbIvTablePath is not null ? Path.GetFileName(gamedbIvTablePath) : null;
        var path = $"/api/jobs/{Uri.EscapeDataString(jobId)}/reencrypt";

        using var response = await SendAsync(b =>
        {
            HttpContent content;
            if (fileBytes is not null)
            {
                var form = new MultipartFormDataContent();
                AddFile(form, "file", fileBytes, modifiedFileName ?? "modified.sqlite");

                if (ivBytes is not null) AddJson(form, "iv_table", ivBytes, ivName!);
                content = form;
            }
            else content = new StringContent("");
            return Post(b, path, content);
        });
        return await ReadJsonAsync(response);
    }

    public async Task<JsonDocument> EncryptGameDbAsync(string sqlitePath, string sqliteFileName)
    {
        var fileBytes = await File.ReadAllBytesAsync(sqlitePath);
        using var response = await SendAsync(b =>
        {
            var form = new MultipartFormDataContent();
            AddFile(form, "file", fileBytes, sqliteFileName);
            return Post(b, "/api/gamedb/encrypt", form);
        });
        return await ReadJsonAsync(response);
    }

    public async Task DownloadAsync(string jobId, string kind, string destinationPath)
    {

        using var response = await SendAsync(b => Get(b, $"/api/jobs/{Uri.EscapeDataString(jobId)}/download/{Uri.EscapeDataString(kind)}"));
        response.EnsureSuccessStatusCode();
        await using var output = File.Create(destinationPath);
        await response.Content.CopyToAsync(output);
    }

    private static void AddFile(MultipartFormDataContent form, string field, byte[] bytes, string fileName)
    {
        var c = new ByteArrayContent(bytes);
        c.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(c, field, fileName);
    }
    private static void AddJson(MultipartFormDataContent form, string field, byte[] bytes, string fileName)
    {
        var c = new ByteArrayContent(bytes);
        c.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        form.Add(c, field, fileName);
    }
    private static void AddText(MultipartFormDataContent form, string field, string value) =>
        form.Add(new StringContent(value, Encoding.UTF8), field);

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {

            var safe = body.Length > 200 ? body[..200] + "…" : body;
            throw new InvalidOperationException($"Backend returned HTTP {(int)response.StatusCode}: {safe}");
        }
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
    }

    private static async Task<byte[]> ReadBytesAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsByteArrayAsync();
        if (!response.IsSuccessStatusCode)
        {
            var text = Encoding.UTF8.GetString(body);
            var safe = text.Length > 200 ? text[..200] + "..." : text;
            throw new InvalidOperationException($"Backend returned HTTP {(int)response.StatusCode}: {safe}");
        }
        return body;
    }

    public static string? FindError(JsonElement root)
    {
        var scalar = FindString(root, "error", "message", "detail", "reason");
        if (!string.IsNullOrWhiteSpace(scalar)) return scalar;

        foreach (var key in new[] { "errors", "warnings" })
        {
            if (TryFind(root, key, out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var item in arr.EnumerateArray())
                {
                    var s = item.ValueKind == JsonValueKind.String ? item.GetString()
                          : item.ValueKind == JsonValueKind.Object ? FindString(item, "message", "error", "detail", "reason")
                          : null;
                    if (!string.IsNullOrWhiteSpace(s)) parts.Add(s!.Trim());
                }
                if (parts.Count > 0) return string.Join(" | ", parts);
            }
        }
        return null;
    }

    public static string? FindString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
            if (TryFind(root, name, out var value))
                return value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number => value.ToString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => value.ToString(),
                };
        return null;
    }

    private static bool TryFind(JsonElement root, string name, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
                if (TryFind(prop.Value, name, out value))
                    return true;
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
                if (TryFind(item, name, out value))
                    return true;
        }
        value = default;
        return false;
    }

    public void Dispose() => _http.Dispose();
}
