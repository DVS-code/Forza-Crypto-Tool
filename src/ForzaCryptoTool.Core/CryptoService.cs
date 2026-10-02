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
            or DetectedKind.Method22Zip or DetectedKind.ConfigFileEncrypted
            or DetectedKind.LegacyEncrypted or DetectedKind.LegacyZip => true,
        _ => false,
    };

    public static bool RunsLocally(DetectionResult detection) => detection.Legacy is not null;

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
        DetectedKind.LegacyEncrypted or DetectedKind.LegacyZip => Path.GetFileNameWithoutExtension(inputName) + "_decrypted" + Path.GetExtension(inputName),
        _ => inputName + ".out",
    };

    public static string LegacyEncryptOutputName(string inputName, string? originalPath)
    {
        string name = originalPath is not null
            ? Path.GetFileName(originalPath)
            : Path.GetFileNameWithoutExtension(inputName).Replace("_decrypted", "").Replace("-decrypted", "").Replace("_edited", "")
              + Path.GetExtension(inputName);

        if (name.Length == 0 || string.Equals(name, inputName, StringComparison.OrdinalIgnoreCase))
            name = Path.GetFileNameWithoutExtension(inputName) + "_encrypted" + Path.GetExtension(inputName);
        return name;
    }

    public async Task<CryptoResult> DecryptAsync(string inputPath, string outputPath, CancellationToken ct = default)
    {
        if (!File.Exists(inputPath))
            return CryptoResult.Fail($"File not found: {inputPath}");

        var detection = FileDetection.Detect(inputPath);
        if (!CanDecrypt(detection.Kind))
            return CryptoResult.Fail($"{detection.KindLabel} cannot be decrypted (nothing to do, or unsupported).");

        Report($"Detected {detection.KindLabel} ({detection.SizeLabel}).");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        if (detection.Legacy is not null)
            return await Task.Run(() => DecryptLegacy(detection, inputPath, outputPath), ct);

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

    internal sealed record LegacyTarget(LegacyGame Game, LegacyKeyType? KeyType = null);

    internal sealed record LegacyPlan(LegacyContext Context, string? OriginalPath, DetectionResult? Original);

    public static LegacyPlan? PlanLegacyEncrypt(string inputPath, string? originalPath, LegacyTarget? target, out string? error)
    {
        error = null;
        DetectionResult? original = null;
        if (!string.IsNullOrWhiteSpace(originalPath) && File.Exists(originalPath))
        {
            var detected = FileDetection.Detect(originalPath);
            if (detected.Legacy is not null) original = detected;
        }
        if (original is null) originalPath = null;

        if (target is null && original is null) return null;

        var originalContext = original?.Legacy!.Context;
        var game = target?.Game ?? originalContext!.Game;

        var keyType = target?.KeyType
            ?? (originalContext is not null && originalContext.Game == game
                ? originalContext.KeyType
                : InferLegacyKeyType(inputPath, game));

        var context = LegacyKeyStore.Find(game, keyType);
        if (context is null)
        {
            error = $"{LegacyKeyStore.FullName(game)} has no {keyType} key. Available: "
                  + string.Join(", ", LegacyKeyStore.KeyTypes(game)) + ".";
            return null;
        }
        return new LegacyPlan(context, originalPath, original);
    }

    public static LegacyKeyType InferLegacyKeyType(string inputPath, LegacyGame game)
    {
        Span<byte> head = stackalloc byte[16];
        int read;
        using (var stream = File.OpenRead(inputPath))
            read = stream.Read(head);

        var guess = read >= 16 && head.SequenceEqual("SQLite format 3\0"u8) ? LegacyKeyType.GameDB
            : read >= 4 && head[0] == 0xB6 && head[1] == 0xF2 && head[2] == 0x8B && head[3] == 0x4A ? LegacyKeyType.Profile
            : LegacyKeyStore.DefaultKeyType(game);
        return LegacyKeyStore.Find(game, guess) is not null ? guess : LegacyKeyStore.DefaultKeyType(game);
    }

    private CryptoResult DecryptLegacy(DetectionResult detection, string inputPath, string outputPath)
    {
        var info = detection.Legacy!;
        Report($"{LegacyKeyStore.FullName(info.Context.Game)}, {info.Context.KeyType} key — decrypting on this machine.");
        var input = File.ReadAllBytes(inputPath);

        if (detection.Kind == DetectedKind.LegacyZip)
        {
            var archive = LegacyZip.Decrypt(input);
            FileSafety.ReplaceWithBackup(outputPath, archive.Archive);
            var notes = new List<string>();
            if (archive.Unrecognised > 0) notes.Add($"{archive.Unrecognised:N0} left encrypted (no key fits)");
            if (archive.BadBlockMacs > 0) notes.Add($"{archive.BadBlockMacs:N0} block MAC(s) did not verify — the archive may be damaged");
            return CryptoResult.Ok(outputPath,
                $"Archive decrypted to {Path.GetFileName(outputPath)}: {archive.Converted:N0} entries"
                + (notes.Count > 0 ? $" ({string.Join("; ", notes)})." : ", all MACs verified."));
        }

        Report($"IV {info.IvHex}, {info.DataBytes / info.Context.BlockSize:N0} block(s) of 0x{info.Context.BlockSize:X}.");
        var result = LegacyCrypto.Decrypt(input, info);
        FileSafety.ReplaceWithBackup(outputPath, result.Plaintext);
        return CryptoResult.Ok(outputPath,
            $"{info.Context.Label} decrypted to {Path.GetFileName(outputPath)} ({result.Plaintext.Length:N0} bytes"
            + (result.BadBlockMacs > 0
                ? $"; {result.BadBlockMacs:N0} block MAC(s) did not verify — the file may be damaged)."
                : ", all MACs verified)."));
    }

    private CryptoResult EncryptLegacy(LegacyPlan plan, string inputPath, string outputPath)
    {
        var context = plan.Context;
        Report($"{LegacyKeyStore.FullName(context.Game)}, {context.KeyType} key — encrypting on this machine.");
        var input = File.ReadAllBytes(inputPath);

        bool isZip = input.Length >= 4 && input[0] == 'P' && input[1] == 'K' && input[2] == 3 && input[3] == 4;
        if (isZip)
        {
            Dictionary<string, byte[]>? ivs = null;
            if (plan.Original?.Kind == DetectedKind.LegacyZip)
                ivs = LegacyZip.ReadEntryIvs(File.ReadAllBytes(plan.OriginalPath!));

            var archive = LegacyZip.Encrypt(input, context, name => ivs is not null && ivs.TryGetValue(name, out var iv) ? iv : null);
            if (archive.Converted == 0)
                return CryptoResult.Fail("This archive has no deflate-compressed entries to encrypt.");

            var check = LegacyZip.Decrypt(archive.Archive);
            if (check.Converted != archive.Converted || check.BadBlockMacs != 0 || check.Unrecognised != 0)
                return CryptoResult.Fail("Self-check failed: the encrypted archive did not decrypt back cleanly. Nothing was written.");

            FileSafety.ReplaceWithBackup(outputPath, archive.Archive);
            return CryptoResult.Ok(outputPath,
                $"Archive encrypted to {Path.GetFileName(outputPath)}: {archive.Converted:N0} entries, verified by decrypting them back.");
        }

        var iv = plan.Original?.Kind == DetectedKind.LegacyEncrypted ? plan.Original.Legacy!.Iv : LegacyCrypto.RandomIv();
        var encrypted = LegacyCrypto.Encrypt(input, context, iv);

        var roundTrip = LegacyCrypto.TryDecrypt(encrypted);
        if (roundTrip is null || roundTrip.Info.Context != context || roundTrip.BadBlockMacs != 0
            || roundTrip.Plaintext.Length < input.Length
            || !roundTrip.Plaintext.AsSpan(0, input.Length).SequenceEqual(input))
            return CryptoResult.Fail("Self-check failed: the encrypted file did not decrypt back to the input. Nothing was written.");

        FileSafety.ReplaceWithBackup(outputPath, encrypted);
        return CryptoResult.Ok(outputPath,
            $"{context.Label} encrypted to {Path.GetFileName(outputPath)} ({encrypted.Length:N0} bytes, verified by decrypting it back).");
    }

    public async Task<CryptoResult> EncryptAsync(
        string inputPath, string outputPath, string? originalPath = null, CancellationToken ct = default,
        LegacyTarget? legacy = null)
    {
        if (!File.Exists(inputPath))
            return CryptoResult.Fail($"File not found: {inputPath}");

        var plan = PlanLegacyEncrypt(inputPath, originalPath, legacy, out var planError);
        if (planError is not null) return CryptoResult.Fail(planError);
        if (plan is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            return await Task.Run(() => EncryptLegacy(plan, inputPath, outputPath), ct);
        }

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
