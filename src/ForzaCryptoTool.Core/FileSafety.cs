using System.Security.Cryptography;

namespace ForzaCryptoTool;

internal sealed record ValidationResult(bool Ok, string Summary, List<string> Issues)
{
    public static ValidationResult Pass(string summary) => new(true, summary, new List<string>());
    public static ValidationResult Fail(string summary, params string[] issues) => new(false, summary, issues.ToList());
}

internal static class FileSafety
{
    private static readonly byte[] SqliteMagic = "SQLite format 3\0"u8.ToArray();

    public sealed record BackupTicket(string OriginalPath, string BackupPath, long OriginalSize, string OriginalSha256);

    public static BackupTicket BeginBackup(string targetPath)
    {
        var info = new FileInfo(targetPath);
        if (!info.Exists)
            throw new FileNotFoundException("Cannot back up a file that does not exist.", targetPath);

        var backupPath = NextBackupPath(targetPath);
        File.Copy(targetPath, backupPath, overwrite: false);

        var sha = Sha256(targetPath);
        Logger.Info($"Backup created ({info.Length:N0} bytes).");
        return new BackupTicket(targetPath, backupPath, info.Length, sha);
    }

    public static void Restore(BackupTicket ticket)
    {
        try
        {
            File.Copy(ticket.BackupPath, ticket.OriginalPath, overwrite: true);
            Logger.Warn("Original file restored from backup after a failed operation.");
        }
        catch (Exception ex)
        {
            Logger.Exception("Rollback failed — backup is preserved", ex);
            throw;
        }
    }

    public static ValidationResult ValidateBeforeWrite(string path, long minSize, long maxSize, DetectedKind expectedKind)
    {
        var issues = new List<string>();
        var info = new FileInfo(path);
        if (!info.Exists)
            return ValidationResult.Fail("File not found", $"Missing: {Path.GetFileName(path)}");

        if (info.Length < minSize)
            issues.Add($"File is smaller than the expected minimum ({info.Length:N0} < {minSize:N0} bytes).");
        if (info.Length > maxSize)
            issues.Add($"File is larger than the expected maximum ({info.Length:N0} > {maxSize:N0} bytes).");

        var detected = FileDetection.Detect(path);
        if (expectedKind != DetectedKind.Unknown && detected.Kind != expectedKind)
            issues.Add($"Expected {expectedKind} but detected {detected.Kind}.");

        return issues.Count == 0
            ? ValidationResult.Pass($"Structure OK ({info.Length:N0} bytes, {detected.KindLabel})")
            : new ValidationResult(false, "Pre-write validation failed", issues);
    }

    public static ValidationResult VerifyAfterWrite(string path, long expectedSize, long sizeTolerance, bool requireSqlite)
    {
        var issues = new List<string>();
        var info = new FileInfo(path);
        if (!info.Exists)
            return ValidationResult.Fail("Output missing", "The output file was not created.");

        if (info.Length == 0)
            issues.Add("Output file is empty.");
        if (expectedSize > 0 && Math.Abs(info.Length - expectedSize) > sizeTolerance)
            issues.Add($"Output size {info.Length:N0} differs from expected {expectedSize:N0} by more than {sizeTolerance:N0} bytes.");

        try
        {
            using var fs = File.OpenRead(path);
            var head = new byte[Math.Min(16, info.Length)];
            _ = fs.Read(head, 0, head.Length);
            if (requireSqlite && !head.AsSpan(0, Math.Min(SqliteMagic.Length, head.Length)).SequenceEqual(SqliteMagic.AsSpan(0, Math.Min(SqliteMagic.Length, head.Length))))
                issues.Add("Output is not a valid SQLite database (missing magic header).");
        }
        catch (Exception ex)
        {
            issues.Add($"Output is not readable: {ex.Message}");
        }

        return issues.Count == 0
            ? ValidationResult.Pass($"Output verified ({info.Length:N0} bytes)")
            : new ValidationResult(false, "Post-write verification failed", issues);
    }

    public static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    public static void ReplaceWithBackup(string targetPath, byte[] newContent)
    {
        string fullPath = Path.GetFullPath(targetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temp = SiblingTempPath(fullPath);
        try
        {
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(newContent);
                output.Flush(flushToDisk: true);
            }
            CommitTemporary(temp, fullPath);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    public static async Task ReplaceWithBackupAsync(
        string targetPath, Stream content, CancellationToken cancellationToken = default)
    {
        string fullPath = Path.GetFullPath(targetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temp = SiblingTempPath(fullPath);
        try
        {
            await using (var output = new FileStream(
                temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await content.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }
            CommitTemporary(temp, fullPath);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static void CommitTemporary(string tempPath, string targetPath)
    {
        if (File.Exists(targetPath))
            File.Replace(tempPath, targetPath, NextBackupPath(targetPath));
        else
            File.Move(tempPath, targetPath);
    }

    private static string SiblingTempPath(string targetPath) =>
        Path.Combine(Path.GetDirectoryName(targetPath)!, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

    private static string NextBackupPath(string targetPath)
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        string candidate = $"{targetPath}.{stamp}.bak";
        for (int suffix = 2; File.Exists(candidate); suffix++)
            candidate = $"{targetPath}.{stamp}_{suffix}.bak";
        return candidate;
    }
}
