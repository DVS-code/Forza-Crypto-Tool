namespace ForzaCryptoTool;

internal sealed class SaveSwapService
{
    private readonly BackendClient _backend;

    public SaveSwapService(BackendClient backend)
    {
        _backend = backend;
    }

    public sealed record Step(string Name, bool Ok, string Detail);

    public sealed record SwapResult(bool Success, bool Blocked, string Message, List<Step> Steps, string? BackupPath);

    private sealed record SwapTarget(string Path, bool Required);

    private const long MinProfileBytes = 64;
    private const long MaxProfileBytes = 64L * 1024 * 1024;

    public async Task<SwapResult> SwapAsync(string donorPath, string activePath, string xuidText)
    {
        var steps = new List<Step>();

        try
        {
            var donorCheck = FileSafety.ValidateBeforeWrite(donorPath, MinProfileBytes, MaxProfileBytes, DetectedKind.ProfileData);
            steps.Add(new Step("Validate donor", donorCheck.Ok, donorCheck.Summary));
            if (!donorCheck.Ok)
                return Fail(steps, "Donor save failed validation.");

            var targets = BuildTargetSet(activePath);
            foreach (var target in targets.Where(t => t.Required || File.Exists(t.Path)))
            {
                var activeCheck = FileSafety.ValidateBeforeWrite(target.Path, MinProfileBytes, MaxProfileBytes, DetectedKind.ProfileData);
                steps.Add(new Step($"Validate {Path.GetFileName(target.Path)}", activeCheck.Ok, activeCheck.Summary));
                if (!activeCheck.Ok)
                    return Fail(steps, "Active save failed validation.");
            }
            steps.Add(new Step("Target set", true, DescribeTargets(targets)));

            if (!ForzaProfile.TryParseXuid(xuidText, out var xuid) || xuid == 0)
            {
                steps.Add(new Step("Parse XUID", false, "XUID is empty or invalid."));
                return Fail(steps, "Enter a valid XUID.");
            }
            steps.Add(new Step("Parse XUID", true, $"XUID 0x{xuid:X16}"));

            var donorSize = new FileInfo(donorPath).Length;
            var sourceXuid = ExtractXuidFromFileName(donorPath);
            steps.Add(new Step("Donor XUID", true,
                sourceXuid is null ? "Not in filename; backend will auto-detect." : sourceXuid));

            var isPgs = IsPgsProfilePath(activePath);
            var profileKind = isPgs ? "pgs" : null;
            var uploadName = isPgs ? "C_ProfileData" : Path.GetFileName(donorPath);

            Logger.Info("Uploading donor profile to backend for server-side swap.");
            var reencrypted = await _backend.SwapProfileAsync(donorPath, uploadName, xuidText, sourceXuid, profileKind);
            steps.Add(new Step("Backend swap", true,
                $"Donor decrypted; embedded XUID patched to 0x{xuid:X16}; re-encrypted server-side."));

            if (reencrypted.Length != donorSize)
            {
                steps.Add(new Step("Size check", false,
                    $"Re-encrypted size {reencrypted.Length:N0} != donor {donorSize:N0}."));
                return Fail(steps, "Re-encrypted save size did not match the donor; aborting before write.");
            }
            steps.Add(new Step("Size check", true, $"{reencrypted.Length:N0} bytes matches donor."));

            if (isPgs)
            {
                steps.Add(new Step("PGS pairing", true,
                    "Replacing C_ProfileData, C_ProfileBackup, and User_*_Backup\\C_ProfileBackup when present."));
            }
            else
            {
                var metaSibling = FindMetaSibling(activePath);
                steps.Add(new Step("Meta pairing", true,
                    metaSibling is null ? "No sibling Meta found." : $"Leaving {Path.GetFileName(metaSibling)} in place (target's)."));
            }

            var tickets = new List<FileSafety.BackupTicket>();
            try
            {
                foreach (var target in targets.Where(t => File.Exists(t.Path)))
                {
                    var ticket = FileSafety.BeginBackup(target.Path);
                    tickets.Add(ticket);
                    steps.Add(new Step($"Backup {Path.GetFileName(target.Path)}", true, Path.GetFileName(ticket.BackupPath)));

                    FileSafety.ReplaceWithBackup(target.Path, reencrypted);

                    var verify = FileSafety.VerifyAfterWrite(target.Path, donorSize, sizeTolerance: 0, requireSqlite: false);
                    steps.Add(new Step($"Verify {Path.GetFileName(target.Path)}", verify.Ok, verify.Summary));
                    if (!verify.Ok)
                    {
                        RestoreAll(tickets, steps);
                        return Fail(steps, "Output verification failed; active save restored from backup.");
                    }
                }
            }
            catch
            {
                RestoreAll(tickets, steps);
                throw;
            }

            Logger.Success("Save swap completed; active save replaced.");
            return new SwapResult(true, false, "Save swap completed successfully.", steps, tickets.FirstOrDefault()?.BackupPath);
        }
        catch (Exception ex)
        {
            Logger.Exception("Save swap failed", ex);
            return Fail(steps, ex.Message);
        }
    }

    public async Task<SwapResult> SwapWithIvsAsync(string donorPath, string donorIvsPath, string activePath, string? activeIvsPath, string xuidText)
    {
        var steps = new List<Step>();
        try
        {
            var donorCheck = FileSafety.ValidateBeforeWrite(donorPath, MinProfileBytes, MaxProfileBytes, DetectedKind.ProfileData);
            steps.Add(new Step("Validate donor", donorCheck.Ok, donorCheck.Summary));
            if (!donorCheck.Ok) return Fail(steps, "Donor save failed validation.");

            steps.Add(new Step("Swap mode", true,
                "Donor mode — re-encrypts under the donor's own T0/IVs (any size); the embedded account XUID " +
                "is set to your target account."));

            var targets = BuildTargetSet(activePath);
            foreach (var target in targets.Where(t => t.Required || File.Exists(t.Path)))
            {
                var activeCheck = FileSafety.ValidateBeforeWrite(target.Path, MinProfileBytes, MaxProfileBytes, DetectedKind.ProfileData);
                steps.Add(new Step($"Validate {Path.GetFileName(target.Path)}", activeCheck.Ok, activeCheck.Summary));
                if (!activeCheck.Ok) return Fail(steps, "Active save failed validation.");
            }
            steps.Add(new Step("Target set", true, DescribeTargets(targets)));

            if (!ForzaProfile.TryParseXuid(xuidText, out var xuid) || xuid == 0)
            {
                steps.Add(new Step("Parse XUID", false, "XUID is empty or invalid."));
                return Fail(steps, "Enter a valid XUID.");
            }
            steps.Add(new Step("Parse XUID", true, $"XUID 0x{xuid:X16}"));

            string? sourceXuid = ExtractXuidFromFileName(donorPath);
            if (sourceXuid is null)
            {
                Logger.Info("Donor XUID not in filename — decrypting donor to locate it.");
                try
                {
                    var donorPlain = await _backend.DecryptProfileWithIvsAsync(
                        donorPath, Path.GetFileName(donorPath), donorIvsPath);
                    var found = Fh6ProfilePlaintext.FindXuid(donorPlain);
                    if (found is not null)
                    {
                        sourceXuid = found.Value.Xuid.ToString();
                        Logger.Info($"Donor XUID detected from plaintext: {sourceXuid}");
                    }
                    else
                    {
                        Logger.Warn("Could not detect donor XUID from plaintext; backend will attempt auto-detect.");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Donor decrypt for XUID detection failed: {ex.Message}. Backend will attempt auto-detect.");
                }
            }
            steps.Add(new Step("Donor XUID", true,
                sourceXuid is null ? "Not detected; backend will auto-detect." : sourceXuid));

            Logger.Info("Swapping donor XUID through the backend (donor mode).");
            var reencrypted = await _backend.SwapProfileXuidWithIvsAsync(
                donorPath, Path.GetFileName(donorPath), donorIvsPath,
                null,
                xuid.ToString(), sourceXuid);

            var encryptedTemp = Path.GetTempFileName();
            try
            {
                await File.WriteAllBytesAsync(encryptedTemp, reencrypted);
                var roundTrip = await _backend.DecryptProfileWithIvsAsync(
                    encryptedTemp, "C_ProfileData", donorIvsPath);
                if (!Fh6ProfilePlaintext.IsFh6Plaintext(roundTrip))
                    return Fail(steps, "Swap output did not decrypt as valid FH6 profile plaintext.");

                int canon = Fh6ProfilePlaintext.CanonicalXuidOffset(roundTrip);
                if (canon < 0)
                    return Fail(steps, "Swap output: could not locate the canonical XUID field after decryption.");
                ulong identity = BitConverter.ToUInt64(roundTrip, canon);
                if (identity != xuid)
                    return Fail(steps,
                        $"Swap output identity XUID is 0x{identity:X16}, expected the target 0x{xuid:X16}.");
            }
            finally
            {
                try { File.Delete(encryptedTemp); } catch { }
            }

            if (!string.IsNullOrWhiteSpace(donorIvsPath) && File.Exists(donorIvsPath))
            {
                var donorTable = ProfileIvTable.Read(donorIvsPath);
                if (reencrypted.Length < 0x24
                    || !reencrypted.AsSpan(0, 16).SequenceEqual(donorTable.Chunks[0].DataIv))
                    return Fail(steps, "Re-encrypted output does not use the donor's T0; no save files were changed.");
            }
            else if (reencrypted.Length < 0x24)
                return Fail(steps, "Re-encrypted output is too small; no save files were changed.");
            steps.Add(new Step("Verify swap output", true,
                $"{reencrypted.Length:N0} bytes; round-trip + target XUID verified."));

            var tickets = new List<FileSafety.BackupTicket>();
            try
            {
                foreach (var target in targets.Where(t => File.Exists(t.Path)))
                {
                    var ticket = FileSafety.BeginBackup(target.Path);
                    tickets.Add(ticket);
                    steps.Add(new Step($"Backup {Path.GetFileName(target.Path)}", true, Path.GetFileName(ticket.BackupPath)));
                    FileSafety.ReplaceWithBackup(target.Path, reencrypted);
                    steps.Add(new Step($"Write {Path.GetFileName(target.Path)}", true, $"{reencrypted.Length:N0} bytes written."));
                }
            }
            catch
            {
                RestoreAll(tickets, steps);
                throw;
            }

            Logger.Success("IV-based save swap completed; active save replaced.");
            return new SwapResult(true, false,
                "Save swap completed. CLOSE FH6 before launching so it doesn't autosave over the new file.",
                steps, tickets.FirstOrDefault()?.BackupPath);
        }
        catch (Exception ex)
        {
            Logger.Exception("IV-based save swap failed", ex);
            return Fail(steps, ex.Message);
        }
    }

    public SwapResult RestoreOriginal(string activePath)
    {
        var steps = new List<Step>();
        try
        {
            var targets = BuildTargetSet(activePath);
            int restored = 0;
            foreach (var target in targets)
            {
                var latest = FindLatestBackup(target.Path);
                if (latest is null)
                {
                    steps.Add(new Step($"Restore {Path.GetFileName(target.Path)}", File.Exists(target.Path) ? true : false,
                        File.Exists(target.Path) ? "no backup found (left as-is)" : "no backup and no live file"));
                    continue;
                }

                if (File.Exists(target.Path))
                {
                    try { File.Copy(target.Path, $"{target.Path}.{DateTime.Now:yyyyMMdd_HHmmss}.prerestore.bak", overwrite: false); } catch { }
                }
                File.Copy(latest, target.Path, overwrite: true);
                restored++;
                steps.Add(new Step($"Restore {Path.GetFileName(target.Path)}", true,
                    $"from {Path.GetFileName(latest)} ({new FileInfo(target.Path).Length:N0} bytes)"));
            }
            if (restored == 0)
                return new SwapResult(false, false, "No pre-swap backups found for this save. Nothing was changed.", steps, null);
            Logger.Success($"Restored {restored} profile file(s) from pre-swap backups.");
            return new SwapResult(true, false,
                $"Original save restored ({restored} file(s)). CLOSE FH6 before launching so it doesn't autosave over the restored file.",
                steps, null);
        }
        catch (Exception ex)
        {
            Logger.Exception("Restore original save failed", ex);
            return Fail(steps, ex.Message);
        }
    }

    private static string? FindLatestBackup(string targetPath)
    {
        var dir = Path.GetDirectoryName(targetPath);
        var name = Path.GetFileName(targetPath);
        if (dir is null || !Directory.Exists(dir)) return null;
        return Directory.EnumerateFiles(dir, $"{name}.*.bak")
            .Where(p => !p.EndsWith(".prerestore.bak", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => File.GetLastWriteTimeUtc(p))
            .FirstOrDefault();
    }

    public bool HasBackup(string activePath)
    {
        try { return BuildTargetSet(activePath).Any(t => FindLatestBackup(t.Path) is not null); }
        catch { return false; }
    }

    private static int ProfileChunkCount(string path)
    {
        const int header = 0x24, chunk = 0x210;
        try
        {
            long len = new FileInfo(path).Length;
            if (len < header + chunk || (len - header) % chunk != 0) return 0;
            return (int)((len - header) / chunk);
        }
        catch { return 0; }
    }

    private static string? ExtractXuidFromFileName(string path)
    {
        var name = Path.GetFileName(path);
        int i = 0;
        while (i < name.Length && char.IsDigit(name[i])) i++;
        return i >= 15 ? name[..i] : null;
    }

    private static string? FindMetaSibling(string activePath)
    {
        var dir = Path.GetDirectoryName(activePath);
        var xuid = ExtractXuidFromFileName(activePath);
        if (dir is null || xuid is null) return null;
        var meta = Path.Combine(dir, $"{xuid}Meta");
        return File.Exists(meta) ? meta : null;
    }

    private static bool IsPgsProfilePath(string path)
    {
        var name = Path.GetFileName(path);
        return (name.Equals("C_ProfileData", StringComparison.OrdinalIgnoreCase)
            || name.Equals("C_ProfileBackup", StringComparison.OrdinalIgnoreCase))
            && path.Contains(@"\ContainersRoot\", StringComparison.OrdinalIgnoreCase);
    }

    private static List<SwapTarget> BuildTargetSet(string activePath)
    {
        if (!IsPgsProfilePath(activePath))
            return new List<SwapTarget> { new(activePath, true) };

        var dir = Path.GetDirectoryName(activePath) ?? "";
        var dirName = Path.GetFileName(dir);
        var root = Directory.GetParent(dir)?.FullName ?? dir;
        var mainDir = dirName.EndsWith("_Backup", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(root, dirName[..^"_Backup".Length])
            : dir;
        var backupDir = mainDir + "_Backup";

        return new[]
        {
            new SwapTarget(Path.Combine(mainDir, "C_ProfileData"), true),
            new SwapTarget(Path.Combine(mainDir, "C_ProfileBackup"), false),
            new SwapTarget(Path.Combine(backupDir, "C_ProfileBackup"), false),
        }
        .GroupBy(t => t.Path, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.First())
        .ToList();
    }

    private static string DescribeTargets(IReadOnlyList<SwapTarget> targets)
    {
        var existing = targets.Count(t => File.Exists(t.Path));
        return targets.Count == 1
            ? Path.GetFileName(targets[0].Path)
            : $"{existing}/{targets.Count} PGS profile files present.";
    }

    private static void RestoreAll(IEnumerable<FileSafety.BackupTicket> tickets, List<Step> steps)
    {
        foreach (var ticket in tickets.Reverse())
        {
            FileSafety.Restore(ticket);
            steps.Add(new Step("Rollback", true, $"{Path.GetFileName(ticket.OriginalPath)} restored."));
        }
    }

    private static SwapResult Fail(List<Step> steps, string message) =>
        new(false, false, message, steps, null);
}
