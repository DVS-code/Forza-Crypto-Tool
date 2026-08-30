namespace ForzaCryptoTool;

internal sealed class SaveSwapService
{
    private readonly BackendClient _backend;

    public SaveSwapService(BackendClient backend) => _backend = backend;

    public event Action<string>? Progress;
    private void Report(string m) { Logger.Info(m); Progress?.Invoke(m); }

    public sealed record Step(string Name, bool Ok, string Detail);

    public sealed record SwapResult(bool Success, string Message, List<Step> Steps, string? BackupPath)
    {
        public static SwapResult Fail(List<Step> steps, string message) => new(false, message, steps, null);
    }

    private const long MinProfileBytes = 64;
    private const long MaxProfileBytes = 64L * 1024 * 1024;

    public async Task<SwapResult> SwapAsync(string donorPath, string activePath, string? targetXuid)
    {
        var steps = new List<Step>();
        try
        {

            bool isRune = RuneProfile.IsRunePath(activePath);
            if (isRune && string.IsNullOrWhiteSpace(targetXuid))
            {
                targetXuid = RuneProfile.XuidDecimal;
                steps.Add(new Step("RUNE detected", true,
                    $"Using RUNE's fixed account XUID {RuneProfile.XuidDecimal} (0x{RuneProfile.XuidHex})."));
            }

            if (!ForzaProfile.TryParseXuid(targetXuid ?? "", out var xuid) || xuid == 0)
            {
                steps.Add(new Step("Parse XUID", false, "XUID is empty or invalid."));
                return SwapResult.Fail(steps, "Enter a valid target XUID.");
            }
            steps.Add(new Step("Target XUID", true, $"{xuid} (0x{xuid:X16})"));

            var donorCheck = FileSafety.ValidateBeforeWrite(donorPath, MinProfileBytes, MaxProfileBytes, DetectedKind.ProfileData);
            steps.Add(new Step("Validate donor", donorCheck.Ok, donorCheck.Summary));
            if (!donorCheck.Ok) return SwapResult.Fail(steps, "Donor save failed validation.");

            var targets = BuildTargetSet(activePath);
            foreach (var target in targets.Where(t => t.Required || File.Exists(t.Path)))
            {
                var check = FileSafety.ValidateBeforeWrite(target.Path, MinProfileBytes, MaxProfileBytes, DetectedKind.ProfileData);
                steps.Add(new Step($"Validate {Path.GetFileName(target.Path)}", check.Ok, check.Summary));
                if (!check.Ok) return SwapResult.Fail(steps, "Active save failed validation.");
            }
            steps.Add(new Step("Target set", true, DescribeTargets(targets)));

            string? sourceXuid = ExtractXuidFromFileName(donorPath);
            if (sourceXuid is null)
            {
                Report("Reading the donor's account id...");
                try
                {
                    var donorPlain = await _backend.DecryptProfileWithIvsAsync(donorPath, Path.GetFileName(donorPath), "");
                    var found = Fh6ProfilePlaintext.FindXuid(donorPlain);
                    if (found is not null)
                    {
                        sourceXuid = found.Value.Xuid.ToString();
                        Logger.Info($"Donor XUID detected from plaintext: {sourceXuid}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Donor decrypt for XUID detection failed: {ex.Message}. Backend will auto-detect.");
                }
            }
            steps.Add(new Step("Donor XUID", true, sourceXuid ?? "not detected; backend will auto-detect"));

            Report("Swapping the save on the backend...");
            var reencrypted = await _backend.SwapProfileXuidWithIvsAsync(
                donorPath, Path.GetFileName(donorPath), "", null, xuid.ToString(), sourceXuid);

            if (reencrypted.Length < 0x24)
                return SwapResult.Fail(steps, "Backend returned a truncated save; nothing was changed.");

            Report("Verifying the swapped save...");
            var verify = await VerifySwapOutputAsync(reencrypted, xuid);
            steps.Add(new Step("Verify swap output", verify.Ok, verify.Detail));
            if (!verify.Ok) return SwapResult.Fail(steps, verify.Detail);

            var tickets = new List<FileSafety.BackupTicket>();
            try
            {
                foreach (var target in targets.Where(t => File.Exists(t.Path)))
                {
                    var ticket = FileSafety.BeginBackup(target.Path);
                    tickets.Add(ticket);
                    FileSafety.ReplaceWithBackup(target.Path, reencrypted);
                    steps.Add(new Step($"Write {Path.GetFileName(target.Path)}", true,
                        $"{reencrypted.Length:N0} bytes (backup: {Path.GetFileName(ticket.BackupPath)})"));
                }
            }
            catch
            {
                RestoreAll(tickets, steps);
                throw;
            }

            if (tickets.Count == 0)
                return SwapResult.Fail(steps, "No save files were found to replace at the target path.");

            Logger.Success("Save swap completed.");
            return new SwapResult(true,
                "Save swap completed. Close FH6 before launching so it doesn't autosave over the new file.",
                steps, tickets[0].BackupPath);
        }
        catch (Exception ex)
        {
            Logger.Exception("Save swap failed", ex);
            return SwapResult.Fail(steps, ex.Message);
        }
    }

    private async Task<(bool Ok, string Detail)> VerifySwapOutputAsync(byte[] reencrypted, ulong expectedXuid)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"fct_verify_{Guid.NewGuid():N}");
        try
        {
            await File.WriteAllBytesAsync(temp, reencrypted);
            var plain = await _backend.DecryptProfileWithIvsAsync(temp, "C_ProfileData", "");

            if (!Fh6ProfilePlaintext.IsFh6Plaintext(plain))
                return (false, "Swap output did not decrypt as a valid FH6 profile.");

            int offset = Fh6ProfilePlaintext.CanonicalXuidOffset(plain);
            if (offset < 0)
                return (false, "Could not locate the identity field in the swapped save.");

            ulong identity = BitConverter.ToUInt64(plain, offset);
            if (identity != expectedXuid)
                return (false, $"Swapped save identity is 0x{identity:X16}, expected 0x{expectedXuid:X16}.");

            return (true, $"{reencrypted.Length:N0} bytes; decrypts cleanly, identity = {expectedXuid}.");
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    public SwapResult RestoreOriginal(string activePath)
    {
        var steps = new List<Step>();
        try
        {
            int restored = 0;
            foreach (var target in BuildTargetSet(activePath))
            {
                var latest = FindLatestBackup(target.Path);
                if (latest is null)
                {
                    steps.Add(new Step($"Restore {Path.GetFileName(target.Path)}", true, "no backup found (left as-is)"));
                    continue;
                }
                if (File.Exists(target.Path))
                {
                    try { File.Copy(target.Path, $"{target.Path}.{DateTime.Now:yyyyMMdd_HHmmss}.prerestore.bak"); }
                    catch { }
                }
                File.Copy(latest, target.Path, overwrite: true);
                restored++;
                steps.Add(new Step($"Restore {Path.GetFileName(target.Path)}", true, $"from {Path.GetFileName(latest)}"));
            }

            if (restored == 0)
                return SwapResult.Fail(steps, "No pre-swap backups found for this save. Nothing was changed.");

            Logger.Success($"Restored {restored} profile file(s).");
            return new SwapResult(true,
                $"Original save restored ({restored} file(s)). Close FH6 before launching.", steps, null);
        }
        catch (Exception ex)
        {
            Logger.Exception("Restore failed", ex);
            return SwapResult.Fail(steps, ex.Message);
        }
    }

    public bool HasBackup(string activePath)
    {
        try { return BuildTargetSet(activePath).Any(t => FindLatestBackup(t.Path) is not null); }
        catch { return false; }
    }

    private static string? FindLatestBackup(string targetPath)
    {
        var dir = Path.GetDirectoryName(targetPath);
        var name = Path.GetFileName(targetPath);
        if (dir is null || !Directory.Exists(dir)) return null;
        return Directory.EnumerateFiles(dir, $"{name}.*.bak")
            .Where(p => !p.EndsWith(".prerestore.bak", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private sealed record SwapTarget(string Path, bool Required);

    private static List<SwapTarget> BuildTargetSet(string activePath)
    {
        if (!IsContainerProfilePath(activePath))
            return new List<SwapTarget> { new(activePath, true) };

        var dir = Path.GetDirectoryName(activePath) ?? "";
        var dirName = Path.GetFileName(dir);
        var root = Directory.GetParent(dir)?.FullName ?? dir;
        var mainDir = dirName.EndsWith("_Backup", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(root, dirName[..^"_Backup".Length])
            : dir;

        return new[]
        {
            new SwapTarget(Path.Combine(mainDir, "C_ProfileData"), true),
            new SwapTarget(Path.Combine(mainDir, "C_ProfileBackup"), false),
            new SwapTarget(Path.Combine(mainDir + "_Backup", "C_ProfileBackup"), false),
        }
        .GroupBy(t => t.Path, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.First())
        .ToList();
    }

    private static bool IsContainerProfilePath(string path)
    {
        var name = Path.GetFileName(path);
        return (name.Equals("C_ProfileData", StringComparison.OrdinalIgnoreCase)
             || name.Equals("C_ProfileBackup", StringComparison.OrdinalIgnoreCase))
            && path.Contains(@"\ContainersRoot\", StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribeTargets(IReadOnlyList<SwapTarget> targets)
    {
        var existing = targets.Count(t => File.Exists(t.Path));
        return targets.Count == 1
            ? Path.GetFileName(targets[0].Path)
            : $"{existing} of {targets.Count} container files present.";
    }

    private static string? ExtractXuidFromFileName(string path)
    {
        var name = Path.GetFileName(path);
        int i = 0;
        while (i < name.Length && char.IsDigit(name[i])) i++;
        return i >= 15 ? name[..i] : null;
    }

    private static void RestoreAll(IEnumerable<FileSafety.BackupTicket> tickets, List<Step> steps)
    {
        foreach (var ticket in tickets.Reverse())
        {
            FileSafety.Restore(ticket);
            steps.Add(new Step("Rollback", true, $"{Path.GetFileName(ticket.OriginalPath)} restored."));
        }
    }
}
