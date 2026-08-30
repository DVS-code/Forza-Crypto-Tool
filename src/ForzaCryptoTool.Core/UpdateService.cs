using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace ForzaCryptoTool;

internal sealed record UpdateInfo(Version Version, string Tag, string Notes, string DownloadUrl, string AssetName);

internal enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    NoReleases,
    NetworkError,
}

internal sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo? Info = null);

internal static class UpdateService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        c.DefaultRequestHeaders.UserAgent.ParseAdd($"ForzaCryptoTool/{BuildConfig.AppVersion}");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public static async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
        => (await CheckAsync(ct).ConfigureAwait(false)).Info;

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"https://api.github.com/repos/{BuildConfig.UpdateRepo}/releases/latest";
            using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {

                Logger.Info("Update check: no published GitHub release available (HTTP 404).");
                return new UpdateCheckResult(UpdateCheckStatus.NoReleases);
            }
            if (!resp.IsSuccessStatusCode)
            {
                Logger.Info($"Update check: HTTP {(int)resp.StatusCode} from GitHub (skipping).");
                return new UpdateCheckResult(UpdateCheckStatus.NetworkError);
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var root = doc.RootElement;

            if (root.TryGetProperty("draft", out var d) && d.GetBoolean()) return new UpdateCheckResult(UpdateCheckStatus.NoReleases);
            if (root.TryGetProperty("prerelease", out var p) && p.GetBoolean()) return new UpdateCheckResult(UpdateCheckStatus.NoReleases);

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var latest = ParseVersion(tag);
            if (latest is null) { Logger.Info($"Update check: unparseable tag '{tag}'."); return new UpdateCheckResult(UpdateCheckStatus.NoReleases); }

            if (latest <= BuildConfig.AppVersion)
            {
                Logger.Info($"Update check: up to date ({BuildConfig.AppVersion} >= {latest}).");
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate);
            }

            string? dlUrl = null, assetName = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var aurl = a.TryGetProperty("browser_download_url", out var bu) ? bu.GetString() ?? "" : "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        dlUrl = aurl; assetName = name; break;
                    }
                    if (dlUrl is null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        dlUrl = aurl; assetName = name;
                    }
                }
            }
            if (dlUrl is null || assetName is null)
            {
                Logger.Info("Update check: newer release found but no .exe/.zip asset to download.");
                return new UpdateCheckResult(UpdateCheckStatus.NoReleases);
            }

            var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            Logger.Info($"Update available: {latest} (tag {tag}).");
            return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable,
                new UpdateInfo(latest, tag, notes, dlUrl, assetName));
        }
        catch (Exception ex)
        {
            Logger.Info($"Update check failed (ignored): {ex.Message}");
            return new UpdateCheckResult(UpdateCheckStatus.NetworkError);
        }
    }

    public static async Task<bool> DownloadAndApplyAsync(UpdateInfo info, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        try
        {
            if (!info.AssetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warn("Update apply: asset is not an .exe; opening the releases page for manual update.");
                OpenReleasesPage();
                return false;
            }

            if (!Uri.TryCreate(info.DownloadUrl, UriKind.Absolute, out var downloadUri)
                || downloadUri.Scheme != Uri.UriSchemeHttps
                || !downloadUri.Host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Warn("Update apply: rejected a non-GitHub or non-HTTPS asset URL.");
                return false;
            }

            var currentExe = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("cannot resolve current exe path");
            var dir = Path.GetDirectoryName(currentExe)!;
            var newExe = Path.Combine(dir, $"ForzaCryptoTool.update-{info.Version}.exe");

            using (var resp = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? -1L;
                await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var dst = File.Create(newExe);
                var buf = new byte[81920];
                long read = 0; int n;
                while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    read += n;
                    if (total > 0) progress?.Report((int)(read * 100 / total));
                }
            }
            var downloaded = new FileInfo(newExe);
            bool validPe = downloaded.Exists && downloaded.Length >= 1024 * 1024;
            if (validPe)
            {
                using var check = File.OpenRead(newExe);
                validPe = check.ReadByte() == 'M' && check.ReadByte() == 'Z';
            }
            if (!validPe)
            {
                try { File.Delete(newExe); } catch { }
                throw new InvalidDataException("The downloaded update is not a valid Windows executable.");
            }
            Logger.Info($"Update downloaded to {newExe}.");

            WriteAndRunSwapScript(currentExe, newExe);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Exception("Update apply failed", ex);
            return false;
        }
    }

    public static void OpenReleasesPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo($"https://github.com/{BuildConfig.UpdateRepo}/releases/latest")
            { UseShellExecute = true });
        }
        catch { }
    }

    private static void WriteAndRunSwapScript(string currentExe, string newExe)
    {
        var pid = Environment.ProcessId;
        var script = Path.Combine(Path.GetTempPath(), $"fct_update_{Guid.NewGuid():N}.cmd");

        var body =
            "@echo off\r\n" +
            $":waitloop\r\n" +
            $"tasklist /FI \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul\r\n" +
            "if not errorlevel 1 (\r\n" +
            "  ping -n 2 127.0.0.1 >nul\r\n" +
            "  goto waitloop\r\n" +
            ")\r\n" +
            "set /a tries=0\r\n" +
            ":copyloop\r\n" +
            $"copy /y \"{newExe}\" \"{currentExe}\" >nul\r\n" +
            "if errorlevel 1 (\r\n" +
            "  set /a tries+=1\r\n" +
            "  if %tries% lss 10 ( ping -n 2 127.0.0.1 >nul & goto copyloop )\r\n" +
            ")\r\n" +
            $"del /q \"{newExe}\" >nul 2>&1\r\n" +
            $"start \"\" \"{currentExe}\"\r\n" +
            "del /q \"%~f0\" >nul 2>&1\r\n";
        File.WriteAllText(script, body);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
        Logger.Info("Update swap script launched; app will exit to complete the update.");
    }

    private static Version? ParseVersion(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var s = tag.Trim().TrimStart('v', 'V');

        var dash = s.IndexOf('-');
        if (dash >= 0) s = s[..dash];
        return Version.TryParse(s.Contains('.') ? s : s + ".0", out var v) ? v : null;
    }
}
