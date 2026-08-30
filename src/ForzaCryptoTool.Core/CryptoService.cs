using System.Text.Json;

namespace ForzaCryptoTool;

internal sealed class CryptoService
{
    private readonly BackendClient _backend;

    public CryptoService(BackendClient backend) => _backend = backend;

    public event Action<string>? Progress;

    private void Report(string message)
    {
        Logger.Info(message);
        Progress?.Invoke(message);
    }

    public sealed record CryptoResult(bool Success, string? OutputPath, string Message)
    {
        public static CryptoResult Ok(string path, string message) => new(true, path, message);
        public static CryptoResult Fail(string message) => new(false, null, message);
    }

    public static bool CanDecrypt(DetectedKind kind) => kind switch
    {
        DetectedKind.GameDbEncrypted or DetectedKind.ProfileData
            or DetectedKind.Method22Zip or DetectedKind.ConfigFileEncrypted => true,
        _ => false,
    };

    public static bool CanEncrypt(DetectedKind kind) => kind switch
    {
        DetectedKind.GameDbDecrypted or DetectedKind.ProfileDecrypted
            or DetectedKind.ConfigFileDecrypted or DetectedKind.Method22ZipDecrypted => true,
        _ => false,
    };

    public static bool NeedsOriginalToEncrypt(DetectedKind kind) => kind switch
    {
        DetectedKind.ConfigFileDecrypted or DetectedKind.Method22ZipDecrypted => true,
        _ => false,
    };

    public static string DefaultOutputName(DetectedKind kind, string inputName) => kind switch
    {
        DetectedKind.GameDbEncrypted => Path.GetFileNameWithoutExtension(inputName) + "_decrypted.sqlite",
        DetectedKind.GameDbDecrypted => "gamedbRC.slt",
        DetectedKind.ProfileData => inputName + "_decrypted.bin",
        DetectedKind.ProfileDecrypted => "C_ProfileData",
        DetectedKind.Method22Zip => Path.GetFileNameWithoutExtension(inputName) + "_decrypted.zip",
        DetectedKind.Method22ZipDecrypted => Path.GetFileNameWithoutExtension(inputName) + "_reencrypted.zip",
        DetectedKind.ConfigFileEncrypted => Path.GetFileNameWithoutExtension(inputName) + "_decrypted" + Path.GetExtension(inputName),
        DetectedKind.ConfigFileDecrypted => Path.GetFileNameWithoutExtension(inputName).Replace("_decrypted", "").Replace("_edited", "") + Path.GetExtension(inputName),
        _ => inputName + ".out",
    };

    public async Task<CryptoResult> DecryptAsync(string inputPath, string outputPath, CancellationToken ct = default)
    {
        if (!File.Exists(inputPath))
            return CryptoResult.Fail($"File not found: {inputPath}");

        var detection = FileDetection.Detect(inputPath);
        if (!CanDecrypt(detection.Kind))
            return CryptoResult.Fail($"{detection.KindLabel} cannot be decrypted (nothing to do, or unsupported).");

        Report($"Detected {detection.KindLabel} ({detection.SizeLabel}).");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        return detection.Kind switch
        {
            DetectedKind.GameDbEncrypted => await DecryptGameDbAsync(inputPath, outputPath, ct),
            DetectedKind.ProfileData => await DecryptProfileAsync(inputPath, outputPath),
            DetectedKind.Method22Zip => await DecryptMethod22Async(inputPath, outputPath, ct),
            DetectedKind.ConfigFileEncrypted => await DecryptConfigAsync(inputPath, outputPath),
            _ => CryptoResult.Fail("Unsupported file type."),
        };
    }

    private async Task<CryptoResult> DecryptGameDbAsync(string inputPath, string outputPath, CancellationToken ct)
    {
        Report("Uploading GameDB to the backend...");
        using var job = await _backend.UploadAsync(inputPath, Path.GetFileName(inputPath));
        var jobId = BackendClient.FindString(job.RootElement, "job_id", "id");
        if (jobId is null) return CryptoResult.Fail("Backend did not return a job id.");

        var status = await PollAsync(jobId, "decrypted", ct);
        if (!status.Ok) return CryptoResult.Fail(status.Message);

        Report("Downloading decrypted SQLite...");
        await _backend.DownloadAsync(jobId, "decrypted", outputPath, ct);
        return CryptoResult.Ok(outputPath, $"GameDB decrypted to {Path.GetFileName(outputPath)}.");
    }

    private async Task<CryptoResult> DecryptProfileAsync(string inputPath, string outputPath)
    {
        Report("Decrypting profile (IVs derived server-side)...");

        var plaintext = await _backend.DecryptProfileWithIvsAsync(inputPath, Path.GetFileName(inputPath), "");
        if (plaintext.Length == 0)
            return CryptoResult.Fail("Backend returned an empty profile plaintext.");

        if (!Fh6ProfilePlaintext.IsFh6Plaintext(plaintext))
            return CryptoResult.Fail("Backend returned data that is not a valid FH6 profile plaintext.");

        _ = Fh6ProfileEditorDocument.Parse(plaintext);

        FileSafety.ReplaceWithBackup(outputPath, plaintext);
        return CryptoResult.Ok(outputPath, $"Profile decrypted and validated ({plaintext.Length:N0} bytes).");
    }

    private async Task<CryptoResult> DecryptMethod22Async(string inputPath, string outputPath, CancellationToken ct)
    {
        Report("Uploading Method 22 archive...");
        using var job = await _backend.DecryptMethod22Async(inputPath, Path.GetFileName(inputPath));
        var jobId = BackendClient.FindString(job.RootElement, "job_id", "id");
        if (jobId is null) return CryptoResult.Fail("Backend did not return a job id.");

        var status = await PollAsync(jobId, "decrypted", ct);
        if (!status.Ok) return CryptoResult.Fail(status.Message);

        Report("Downloading decrypted archive...");
        await _backend.DownloadAsync(jobId, "method22-decrypted", outputPath, ct);
        return CryptoResult.Ok(outputPath, $"Method 22 archive decrypted to {Path.GetFileName(outputPath)}.");
    }

    private async Task<CryptoResult> DecryptConfigAsync(string inputPath, string outputPath)
    {
        Report("Decrypting config file...");
        var plain = await _backend.DecryptConfigFileAsync(inputPath, Path.GetFileName(inputPath));
        FileSafety.ReplaceWithBackup(outputPath, plain);
        return CryptoResult.Ok(outputPath, $"Config decrypted to {Path.GetFileName(outputPath)} ({plain.Length:N0} bytes).");
    }

    public async Task<CryptoResult> EncryptAsync(
        string inputPath, string outputPath, string? originalPath = null, CancellationToken ct = default)
    {
        if (!File.Exists(inputPath))
            return CryptoResult.Fail($"File not found: {inputPath}");

        var detection = FileDetection.Detect(inputPath);
        if (!CanEncrypt(detection.Kind))
            return CryptoResult.Fail($"{detection.KindLabel} cannot be re-encrypted.");

        if (NeedsOriginalToEncrypt(detection.Kind))
        {
            if (string.IsNullOrWhiteSpace(originalPath) || !File.Exists(originalPath))
                return CryptoResult.Fail(
                    $"{detection.KindLabel} needs the ORIGINAL encrypted file to re-encrypt (pass --original).");
        }

        Report($"Detected {detection.KindLabel} ({detection.SizeLabel}).");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        return detection.Kind switch
        {
            DetectedKind.GameDbDecrypted => await EncryptGameDbAsync(inputPath, outputPath, ct),
            DetectedKind.ProfileDecrypted => await EncryptProfileAsync(inputPath, outputPath),
            DetectedKind.ConfigFileDecrypted => await EncryptConfigAsync(inputPath, outputPath, originalPath!),
            DetectedKind.Method22ZipDecrypted => await EncryptMethod22Async(inputPath, outputPath, originalPath!, ct),
            _ => CryptoResult.Fail("Unsupported file type."),
        };
    }

    private async Task<CryptoResult> EncryptGameDbAsync(string inputPath, string outputPath, CancellationToken ct)
    {
        Report("Uploading edited SQLite for re-encryption...");
        using var job = await _backend.EncryptGameDbAsync(inputPath, Path.GetFileName(inputPath));
        var jobId = BackendClient.FindString(job.RootElement, "job_id", "id");
        if (jobId is null) return CryptoResult.Fail("Backend did not return a job id.");

        var status = await PollAsync(jobId, "reencrypted", ct);
        if (!status.Ok) return CryptoResult.Fail(status.Message);

        Report("Downloading re-encrypted GameDB...");
        await _backend.DownloadAsync(jobId, "encrypted", outputPath, ct);
        return CryptoResult.Ok(outputPath, $"GameDB re-encrypted to {Path.GetFileName(outputPath)}.");
    }

    private async Task<CryptoResult> EncryptProfileAsync(string inputPath, string outputPath)
    {
        Report("Re-encrypting profile (IVs derived server-side)...");
        var encrypted = await _backend.ReencryptProfileWithIvsAsync(inputPath, Path.GetFileName(inputPath), "");
        if (encrypted.Length <= 0x24 || (encrypted.Length - 0x24) % 0x210 != 0)
            return CryptoResult.Fail("Backend returned invalid FH6 encrypted-profile framing.");

        FileSafety.ReplaceWithBackup(outputPath, encrypted);
        return CryptoResult.Ok(outputPath, $"Profile re-encrypted ({encrypted.Length:N0} bytes).");
    }

    private async Task<CryptoResult> EncryptConfigAsync(string inputPath, string outputPath, string originalPath)
    {
        Report("Re-encrypting config file...");
        var encrypted = await _backend.ReencryptConfigFileAsync(inputPath, Path.GetFileName(inputPath), originalPath);
        FileSafety.ReplaceWithBackup(outputPath, encrypted);
        return CryptoResult.Ok(outputPath, $"Config re-encrypted to {Path.GetFileName(outputPath)} ({encrypted.Length:N0} bytes).");
    }

    private async Task<CryptoResult> EncryptMethod22Async(string inputPath, string outputPath, string originalPath, CancellationToken ct)
    {
        Report("Uploading edited archive for re-encryption...");
        using var job = await _backend.ReencryptMethod22Async(inputPath, Path.GetFileName(inputPath), originalPath);
        var jobId = BackendClient.FindString(job.RootElement, "job_id", "id");
        if (jobId is null) return CryptoResult.Fail("Backend did not return a job id.");

        var status = await PollAsync(jobId, "reencrypted", ct);
        if (!status.Ok) return CryptoResult.Fail(status.Message);

        Report("Downloading re-encrypted archive...");
        await _backend.DownloadAsync(jobId, "method22-reencrypted", outputPath, ct);
        return CryptoResult.Ok(outputPath, $"Method 22 archive re-encrypted to {Path.GetFileName(outputPath)}.");
    }

    private sealed record PollResult(bool Ok, string Message);

    private async Task<PollResult> PollAsync(string jobId, string terminalStatus, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMinutes(10);
        int delayMs = 400;
        string? last = null;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(delayMs, ct);
            delayMs = Math.Min(delayMs + 200, 2000);

            using var doc = await _backend.GetJobAsync(jobId);
            var root = doc.RootElement;
            var status = BackendClient.FindString(root, "status", "state")?.ToLowerInvariant();

            if (status != last && status is not null)
            {
                Report($"Job {status}...");
                last = status;
            }

            if (status == terminalStatus)
                return new PollResult(true, "Done.");

            if (status is "failed" or "error")
                return new PollResult(false, BackendClient.FindError(root) ?? "Backend reported failure.");
        }
        return new PollResult(false, "Timed out waiting for the backend job to finish.");
    }
}
