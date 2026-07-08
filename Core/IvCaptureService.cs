using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;

namespace ForzaCryptoTool;

internal sealed class IvCaptureService
{
    public const string ToolExeName = "FH6_Profile_IV_Tool.exe";

    public sealed record CaptureResult(
        bool Success,
        string Message,
        string? OutputDir,
        string? IvsJsonPath,
        string? CapturedProfilePath = null);

    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public static bool RelaunchAsAdmin()
    {
        try
        {
            var exe = Process.GetCurrentProcess().MainModule?.FileName
                      ?? Path.Combine(AppContext.BaseDirectory, "ForzaCryptoTool.exe");
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory,
            };
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Elevation cancelled or failed: {ex.Message}");
            return false;
        }
    }

    public static string? FindToolExe()
    {

        var embedded = ExtractEmbeddedTool();
        if (embedded is not null) return embedded;

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, ToolExeName),
            Path.Combine(AppContext.BaseDirectory, "tools", ToolExeName),
            @"C:\Users\gfren\Documents\FH6 MODS\FH6 RE\Tools\profile_iv_tool\launcher\target\release\" + ToolExeName,
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private const string EmbeddedResourceName = "ForzaCryptoTool.Tools.FH6_Profile_IV_Tool.exe";

    private static string? ExtractEmbeddedTool()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(EmbeddedResourceName);
            if (stream is null) return null;

            var dir = Path.Combine(Path.GetTempPath(), "ForzaCryptoTool", BuildConfig.AppVersion.ToString());
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, ToolExeName);

            if (File.Exists(dest) && new FileInfo(dest).Length == stream.Length)
                return dest;

            try
            {
                using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
                stream.CopyTo(fs);
                return dest;
            }
            catch (IOException)
            {
                stream.Position = 0;
                var alt = Path.Combine(dir, $"FH6_Profile_IV_Tool_{Guid.NewGuid():N}.exe");
                using var fs = new FileStream(alt, FileMode.Create, FileAccess.Write, FileShare.None);
                stream.CopyTo(fs);
                return alt;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not extract the embedded IV tool: {ex.Message}");
            return null;
        }
    }

    private static string MoveOutputNextToApp(string outputDir)
    {
        try
        {
            var appDir = AppContext.BaseDirectory;
            var capturesRoot = Path.Combine(appDir, "IV Captures");
            Directory.CreateDirectory(capturesRoot);
            var dest = Path.Combine(capturesRoot, Path.GetFileName(outputDir.TrimEnd('\\', '/')));
            if (string.Equals(Path.GetFullPath(outputDir), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                return outputDir;
            if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
            Directory.Move(outputDir, dest);
            return dest;
        }
        catch (Exception ex)
        {

            try
            {
                var appDir = AppContext.BaseDirectory;
                var dest = Path.Combine(appDir, "IV Captures", Path.GetFileName(outputDir.TrimEnd('\\', '/')));
                Directory.CreateDirectory(dest);
                foreach (var f in Directory.GetFiles(outputDir))
                    File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), overwrite: true);
                return dest;
            }
            catch
            {
                Logger.Warn($"Could not relocate IV output next to the app: {ex.Message}");
                return outputDir;
            }
        }
    }

    public static bool IsGameRunning() =>
        Process.GetProcessesByName("forzahorizon6").Length > 0;

    public static void KillGame()
    {
        foreach (var p in Process.GetProcessesByName("forzahorizon6"))
        {
            try { p.Kill(true); p.WaitForExit(5000); }
            catch (Exception ex) { Logger.Warn($"Could not kill forzahorizon6 (pid {p.Id}): {ex.Message}"); }
        }

        foreach (var p in Process.GetProcessesByName("steamclient_loader_x64"))
        {
            try { p.Kill(true); } catch { }
        }
    }

    public async Task<CaptureResult> CaptureAsync(Action<string> progress, CancellationToken ct = default)
    {
        var exe = FindToolExe();
        if (exe is null)
            return new CaptureResult(false, $"{ToolExeName} not found next to the app. Reinstall the tool.", null, null);

        if (!IsAdministrator())
            return new CaptureResult(false, "Administrator rights are required to capture IVs. Relaunch as Administrator.", null, null);

        var preCapture = TakePreCaptureSnapshots();

        Logger.Info("Starting IV capture via the bundled IV Tool.");
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory,
        };

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        string? outputDir = null;
        string? error = null;

        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handle(string raw)
        {
            var line = raw.Trim();
            if (line.Length == 0) return;
            progress(line);
            const string marker = "VERIFIED output:";
            int i = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
            {
                outputDir = line[(i + marker.Length)..].Trim();
                done.TrySetResult(true);
            }
            else if (line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            {
                error = line["ERROR:".Length..].Trim();
                done.TrySetResult(false);
            }
        }

        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) Handle(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) Handle(e.Data); };

        proc.Exited += (_, _) =>
        {
            try { proc.WaitForExit(); } catch { }
            if (outputDir is null) done.TrySetResult(false);
            else done.TrySetResult(true);
        };

        try
        {
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            using var reg = ct.Register(() => done.TrySetResult(false));
            var timeout = Task.Delay(TimeSpan.FromMinutes(10.5), CancellationToken.None);
            var finished = await Task.WhenAny(done.Task, timeout);

            try { await proc.StandardInput.WriteLineAsync(); await proc.StandardInput.FlushAsync(); } catch { }
            try
            {
                if (!proc.WaitForExit(2000)) proc.Kill(true);

                proc.WaitForExit();
            }
            catch { }

            if (ct.IsCancellationRequested)
                return new CaptureResult(false, "Capture cancelled.", null, null);

            if (outputDir is null)
                return new CaptureResult(false,
                    finished == timeout ? "Capture timed out. Close FH6 and try again." : null, null, null);
        }
        catch (Exception ex)
        {
            Logger.Exception("IV tool failed to run", ex);
            return new CaptureResult(false, ex.Message, null, null);
        }

        if (outputDir is not null && Directory.Exists(outputDir))
        {

            KillGame();

            outputDir = MoveOutputNextToApp(outputDir);

            var ivsJson = Path.Combine(outputDir, "profile_ivs.json");
            string? capturedProfile = null;
            if (File.Exists(ivsJson))
            {
                capturedProfile = SnapshotMatchingProfile(outputDir, ivsJson);

                if (capturedProfile is null)
                    capturedProfile = MatchPreCaptureSnapshot(preCapture, outputDir, ivsJson);
            }
            CleanPreCaptureSnapshots(preCapture);
            Logger.Success($"IV capture verified. Output: {outputDir}");
            var snapshotMessage = capturedProfile is not null
                ? " A byte-exact matching C_ProfileData snapshot was saved beside the IV table."
                : " No matching on-disk C_ProfileData snapshot was found; select the file manually and it will be validated before decrypting.";
            return new CaptureResult(true,
                "IV capture verified. FH6 was closed so it cannot autosave over this version." + snapshotMessage,
                outputDir, File.Exists(ivsJson) ? ivsJson : null, capturedProfile);
        }

        CleanPreCaptureSnapshots(preCapture);
        var msg = error ?? "Capture did not complete. Make sure you launched FH6 and pressed Enter on the first screen "
                  + "before the profile finished loading, and that you ran as Administrator.";
        return new CaptureResult(false, msg, null, null);
    }

    private static readonly string PreCaptureDir =
        Path.Combine(Path.GetTempPath(), "ForzaCryptoTool", "pre_capture");

    private static List<string> TakePreCaptureSnapshots()
    {
        var snapshots = new List<string>();
        try
        {
            Directory.CreateDirectory(PreCaptureDir);
            foreach (var old in Directory.GetFiles(PreCaptureDir))
                try { File.Delete(old); } catch { }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not prepare pre-capture directory: {ex.Message}");
            return snapshots;
        }

        foreach (var candidate in SaveLocator.FindCandidates())
        {
            try
            {
                var dest = Path.Combine(PreCaptureDir, $"C_ProfileData_{Path.GetRandomFileName()}.precapture");
                File.Copy(candidate.Path, dest, overwrite: true);
                snapshots.Add(dest);
                Logger.Detail($"Pre-capture: snapshotted {Path.GetFileName(candidate.Path)}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not pre-capture {Path.GetFileName(candidate.Path)}: {ex.Message}");
            }
        }
        if (snapshots.Count == 0)
            Logger.Warn("Pre-capture: no C_ProfileData found to snapshot (save location not detected).");
        else
            Logger.Detail($"Pre-capture: {snapshots.Count} profile snapshot(s) ready.");
        return snapshots;
    }

    private static string? MatchPreCaptureSnapshot(List<string> snapshots, string outputDir, string ivsJsonPath)
    {
        foreach (var snap in snapshots)
        {
            var match = ProfileIvTable.MatchesEncrypted(snap, ivsJsonPath);
            if (!match.Ok) continue;
            try
            {
                var dest = Path.Combine(outputDir, "C_ProfileData.captured");
                File.Copy(snap, dest, overwrite: true);
                Logger.Success("Pre-capture snapshot matched IV table — FH6 autosaved during capture window. "
                    + "Using pre-launch copy as C_ProfileData.captured.");
                return dest;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not copy pre-capture snapshot: {ex.Message}");
            }
        }
        if (snapshots.Count > 0)
            Logger.Warn("Pre-capture snapshots also did not match the IV fingerprints. "
                + "The IVs may have been captured after an in-game autosave. Re-run Capture IVs.");
        return null;
    }

    private static void CleanPreCaptureSnapshots(List<string> snapshots)
    {
        foreach (var s in snapshots)
            try { File.Delete(s); } catch { }
    }

    private static string? SnapshotMatchingProfile(string outputDir, string ivsJsonPath)
    {
        foreach (var candidate in SaveLocator.FindCandidates())
        {
            var match = ProfileIvTable.MatchesEncrypted(candidate.Path, ivsJsonPath);
            if (!match.Ok) continue;
            try
            {
                var dest = Path.Combine(outputDir, "C_ProfileData.captured");
                File.Copy(candidate.Path, dest, overwrite: true);

                var copiedMatch = ProfileIvTable.MatchesEncrypted(dest, ivsJsonPath);
                if (!copiedMatch.Ok)
                {
                    File.Delete(dest);
                    Logger.Warn($"Captured profile copy failed fingerprint validation: {copiedMatch.Detail}");
                    return null;
                }
                File.WriteAllText(Path.Combine(outputDir, "captured_profile_source.txt"), candidate.Path);
                Logger.Success($"Saved matching encrypted profile snapshot: {dest}");
                return dest;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not preserve matching profile snapshot: {ex.Message}");
                return null;
            }
        }
        Logger.Warn("No on-disk C_ProfileData matched the newly captured IV fingerprints.");
        return null;
    }
}

internal static class ProfileIvTable
{
    public enum MatchFailure
    {
        None,
        InvalidFile,
        InvalidIvTable,
        ChunkCountMismatch,
        MissingFingerprints,
        StaleCapture,
    }

    public sealed record ChunkFingerprint(int Chunk, byte[] DataIv, byte[] FirstCiphertextBlock);
    public sealed record Info(int ChunkCount, bool Verified, IReadOnlyList<ChunkFingerprint> Chunks);
    public readonly record struct MatchResult(bool Ok, MatchFailure Failure, string Detail);

    public const string StaleCaptureMessage =
        "These IVs don't match this C_ProfileData. The game saved a newer version after the IVs were captured, "
        + "so the captured IVs are stale. Re-run Create Save > Capture IVs, and as soon as it succeeds "
        + "(FH6 is killed automatically) decrypt the captured snapshot without launching FH6 again. "
        + "Do not let FH6 autosave between capturing and decrypting.";

    private static byte[] ParseHex16(string? value, string field, int chunk)
    {
        if (value is null || value.Length != 32)
            throw new InvalidOperationException($"Chunk {chunk} {field} must be a 16-byte (32 hex) value.");
        try { return Convert.FromHexString(value); }
        catch (FormatException) { throw new InvalidOperationException($"Chunk {chunk} {field} is not valid hexadecimal."); }
    }

    public static Info Read(string jsonPath)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(jsonPath));
        var root = doc.RootElement;
        if (root.ValueKind != System.Text.Json.JsonValueKind.Object || !root.TryGetProperty("chunks", out var chunks)
            || chunks.ValueKind != System.Text.Json.JsonValueKind.Array)
            throw new InvalidOperationException("Not a valid profile_ivs.json (missing 'chunks').");

        int count = chunks.GetArrayLength();
        if (count == 0) throw new InvalidOperationException("IV table is empty.");

        var entries = new List<ChunkFingerprint>(count);
        foreach (var c in chunks.EnumerateArray())
        {
            if (!c.TryGetProperty("chunk", out var ci) || !ci.TryGetInt32(out int idx))
                throw new InvalidOperationException("IV table entries must use explicit 1-based chunk numbers.");
            if (!c.TryGetProperty("data_iv", out var d) || !c.TryGetProperty("mac_iv", out var m))
                throw new InvalidOperationException("IV table entries must have data_iv and mac_iv.");
            var dataIv = ParseHex16(d.GetString(), "data_iv", idx);
            _ = ParseHex16(m.GetString(), "mac_iv", idx);
            if (!c.TryGetProperty("first_ciphertext_block", out var fp))
                throw new InvalidOperationException(
                    "IV table has no first_ciphertext_block fingerprints. It is an old capture; re-run Create Save > Capture IVs.");
            entries.Add(new ChunkFingerprint(idx, dataIv, ParseHex16(fp.GetString(), "first_ciphertext_block", idx)));
        }
        entries.Sort((a, b) => a.Chunk.CompareTo(b.Chunk));
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].Chunk != i + 1)
                throw new InvalidOperationException($"IV table chunk numbering must be contiguous and 1-based; expected chunk {i + 1}.");
        bool verified = root.TryGetProperty("verified", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.True;
        return new Info(count, verified, entries);
    }

    public static MatchResult MatchesEncrypted(string encryptedPath, string ivsJsonPath)
    {
        try
        {
            var info = Read(ivsJsonPath);
            long len = new FileInfo(encryptedPath).Length;
            const int HEADER = 0x24, CHUNK = 0x210;
            if (len < HEADER + CHUNK || (len - HEADER) % CHUNK != 0)
                return new(false, MatchFailure.InvalidFile, "Encrypted file is not a 0x24 + N*0x210 C_ProfileData container.");
            int n = checked((int)((len - HEADER) / CHUNK));
            if (info.ChunkCount != n)
                return new(false, MatchFailure.ChunkCountMismatch,
                    $"IV table has {info.ChunkCount} chunks but the file has {n}. This capture belongs to a different save version.");

            using var file = new FileStream(encryptedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var actual = new byte[16];
            if (file.Read(actual, 0, actual.Length) != actual.Length)
                return new(false, MatchFailure.InvalidFile, "Could not read the C_ProfileData header.");
            if (!actual.AsSpan().SequenceEqual(info.Chunks[0].DataIv))
                return new(false, MatchFailure.StaleCapture, StaleCaptureMessage + " (Header T0 mismatch.)");

            var samples = new[] { 0, n / 2, n - 1 }.Distinct();
            foreach (int zeroBased in samples)
            {
                file.Position = HEADER + (long)zeroBased * CHUNK;
                if (file.Read(actual, 0, actual.Length) != actual.Length)
                    return new(false, MatchFailure.InvalidFile, $"Could not read ciphertext for chunk {zeroBased + 1}.");
                if (!actual.AsSpan().SequenceEqual(info.Chunks[zeroBased].FirstCiphertextBlock))
                    return new(false, MatchFailure.StaleCapture,
                        StaleCaptureMessage + $" (Ciphertext fingerprint mismatch at chunk {zeroBased + 1}.)");
            }
            return new(true, MatchFailure.None,
                $"Exact match: {n} chunks, header T0 and first/middle/last ciphertext fingerprints agree"
                + (info.Verified ? ", capture self-verified." : "."));
        }
        catch (InvalidOperationException ex)
        {
            var kind = ex.Message.Contains("first_ciphertext_block", StringComparison.OrdinalIgnoreCase)
                ? MatchFailure.MissingFingerprints : MatchFailure.InvalidIvTable;
            return new(false, kind, ex.Message);
        }
        catch (Exception ex)
        {
            return new(false, MatchFailure.InvalidFile, ex.Message);
        }
    }
}
