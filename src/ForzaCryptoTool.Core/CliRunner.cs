using System.Text;

namespace ForzaCryptoTool;

internal static class CliRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        var cmd = args[0].ToLowerInvariant().TrimStart('-');
        var rest = args.Skip(1).ToArray();

        if (cmd is "help" or "h" or "?") { PrintHelp(); return ExitCodes.Success; }
        if (cmd is "version" or "v")
        {
            ConsoleHost.Write($"Forza Crypto Tool {BuildConfig.VersionLabel}");
            return ExitCodes.Success;
        }

        var opts = CliOptions.Parse(rest);

        return cmd switch
        {
            "decrypt" or "dec" or "d" => await DecryptAsync(opts),
            "encrypt" or "enc" or "e" => await EncryptAsync(opts),
            "detect" or "info" or "identify" => Detect(opts),
            "saveswap" or "swap" => await SaveSwapAsync(opts),
            "restore" => Restore(opts),
            "saves" or "list-saves" => ListSaves(),
            "profile-inspect" or "profile-info" => ProfileInspect(opts),
            "profile-set" => ProfileSet(opts),
            "profile-xuid" => ProfileXuid(opts),
            "profile-sql" => ProfileSql(opts),
            "profile-binary" => ProfileBinary(opts),
            "profile-bxml-string" => ProfileBxmlString(opts),
            _ => Unknown(cmd),
        };
    }

    private static int Unknown(string cmd)
    {
        ConsoleHost.Error($"unknown command '{cmd}'. Run `ForzaCryptoTool help` for usage.");
        return ExitCodes.BadUsage;
    }

    private static async Task<int> DecryptAsync(CliOptions o)
    {
        if (o.Positional.Count == 0) { ConsoleHost.Error("decrypt needs a file. Usage: decrypt <file> [-o out]"); return ExitCodes.BadUsage; }

        var input = Path.GetFullPath(o.Positional[0]);
        if (!File.Exists(input)) { ConsoleHost.Error($"file not found: {input}"); return ExitCodes.FileNotFound; }

        var detection = FileDetection.Detect(input);
        if (!CryptoService.CanDecrypt(detection.Kind))
        {
            ConsoleHost.Error($"{detection.FileName} is {detection.KindLabel} — nothing to decrypt.");
            return ExitCodes.Unsupported;
        }

        var output = ResolveOutput(o, input, CryptoService.DefaultOutputName(detection.Kind, detection.FileName));
        if (!ConfirmOverwrite(output, o)) return ExitCodes.Failure;

        using var backend = new BackendClient();
        if (await backend.SelectBackendAsync() is null)
        {
            ConsoleHost.Error("backend offline — please try again later.");
            return ExitCodes.BackendOffline;
        }

        var service = new CryptoService(backend);
        service.Progress += ConsoleHost.Write;

        var result = await service.DecryptAsync(input, output);
        if (!result.Success) { ConsoleHost.Error(result.Message); return ExitCodes.Failure; }

        ConsoleHost.Write(result.Message);
        ConsoleHost.Write(result.OutputPath!);
        return ExitCodes.Success;
    }

    private static async Task<int> EncryptAsync(CliOptions o)
    {
        if (o.Positional.Count == 0) { ConsoleHost.Error("encrypt needs a file. Usage: encrypt <file> [-o out] [--original <encrypted>]"); return ExitCodes.BadUsage; }

        var input = Path.GetFullPath(o.Positional[0]);
        if (!File.Exists(input)) { ConsoleHost.Error($"file not found: {input}"); return ExitCodes.FileNotFound; }

        var detection = FileDetection.Detect(input);
        if (!CryptoService.CanEncrypt(detection.Kind))
        {
            ConsoleHost.Error($"{detection.FileName} is {detection.KindLabel} — cannot be re-encrypted.");
            return ExitCodes.Unsupported;
        }

        string? original = o.Get("original", "orig");
        if (CryptoService.NeedsOriginalToEncrypt(detection.Kind))
        {
            if (original is null)
            {
                ConsoleHost.Error($"{detection.KindLabel} needs the original encrypted file: --original <path>");
                return ExitCodes.BadUsage;
            }
            original = Path.GetFullPath(original);
            if (!File.Exists(original)) { ConsoleHost.Error($"original not found: {original}"); return ExitCodes.FileNotFound; }
        }

        var output = ResolveOutput(o, input, CryptoService.DefaultOutputName(detection.Kind, detection.FileName));
        if (!ConfirmOverwrite(output, o)) return ExitCodes.Failure;

        using var backend = new BackendClient();
        if (await backend.SelectBackendAsync() is null)
        {
            ConsoleHost.Error("backend offline — please try again later.");
            return ExitCodes.BackendOffline;
        }

        var service = new CryptoService(backend);
        service.Progress += ConsoleHost.Write;

        var result = await service.EncryptAsync(input, output, original);
        if (!result.Success) { ConsoleHost.Error(result.Message); return ExitCodes.Failure; }

        ConsoleHost.Write(result.Message);
        ConsoleHost.Write(result.OutputPath!);
        return ExitCodes.Success;
    }

    private static int Detect(CliOptions o)
    {
        if (o.Positional.Count == 0) { ConsoleHost.Error("detect needs at least one file."); return ExitCodes.BadUsage; }

        int missing = 0;
        foreach (var raw in o.Positional)
        {
            var path = Path.GetFullPath(raw);
            if (!File.Exists(path)) { ConsoleHost.Error($"file not found: {path}"); missing++; continue; }

            var d = FileDetection.Detect(path);
            var actions = new List<string>();
            if (CryptoService.CanDecrypt(d.Kind)) actions.Add("decrypt");
            if (CryptoService.CanEncrypt(d.Kind)) actions.Add("encrypt");

            ConsoleHost.Write($"{d.FileName}");
            ConsoleHost.Write($"  type      : {d.KindLabel}");
            ConsoleHost.Write($"  size      : {d.SizeLabel} ({d.Size:N0} bytes)");
            ConsoleHost.Write($"  encrypted : {(d.Encrypted ? "yes" : "no")}");
            ConsoleHost.Write($"  supports  : {(actions.Count > 0 ? string.Join(", ", actions) : "nothing (unrecognised)")}");
            if (CryptoService.NeedsOriginalToEncrypt(d.Kind))
                ConsoleHost.Write("  note      : re-encrypting this needs --original <encrypted file>");
        }
        return missing > 0 ? ExitCodes.FileNotFound : ExitCodes.Success;
    }

    private static async Task<int> SaveSwapAsync(CliOptions o)
    {
        if (o.Positional.Count == 0) { ConsoleHost.Error("saveswap needs a donor save. Usage: saveswap <donor> [--rune | --target <path>] [--xuid <n>]"); return ExitCodes.BadUsage; }

        var donor = Path.GetFullPath(o.Positional[0]);
        if (!File.Exists(donor)) { ConsoleHost.Error($"donor not found: {donor}"); return ExitCodes.FileNotFound; }

        string? target = o.Get("target", "t");
        if (target is not null)
        {
            target = Path.GetFullPath(target);
        }
        else
        {
            var wantRune = o.Has("rune");
            var candidates = SaveLocator.FindCandidates()
                .Where(c => !wantRune || c.Flavour == SaveLocator.SaveFlavour.Rune)
                .ToList();

            if (candidates.Count == 0)
            {
                ConsoleHost.Error(wantRune
                    ? "no RUNE save found. Pass --target <path to C_ProfileData> explicitly."
                    : "no FH6 save found. Pass --target <path to C_ProfileData> explicitly.");
                return ExitCodes.FileNotFound;
            }
            if (candidates.Count > 1 && !o.Has("yes", "y"))
            {
                ConsoleHost.Error($"{candidates.Count} saves found — pick one with --target:");
                foreach (var c in candidates) ConsoleHost.Write($"  [{c.Source}] {c.Path}");
                return ExitCodes.BadUsage;
            }
            target = candidates[0].Path;
            ConsoleHost.Write($"Target save: [{candidates[0].Source}] {target}");
        }

        if (!File.Exists(target)) { ConsoleHost.Error($"target save not found: {target}"); return ExitCodes.FileNotFound; }

        string? xuid = o.Get("xuid", "x");
        bool isRune = RuneProfile.IsRunePath(target);
        if (xuid is null && !isRune)
        {
            ConsoleHost.Error("--xuid <target account XUID> is required (not needed for --rune targets).");
            return ExitCodes.BadUsage;
        }

        if (!o.Has("yes", "y"))
        {
            ConsoleHost.Write("");
            ConsoleHost.Write("This will OVERWRITE the save files at:");
            ConsoleHost.Write($"  {Path.GetDirectoryName(target)}");
            ConsoleHost.Write($"donor : {donor}");
            ConsoleHost.Write($"xuid  : {xuid ?? RuneProfile.XuidDecimal + " (RUNE)"}");
            ConsoleHost.Write("Backups are written next to each file. Continue? [y/N] ");
            var answer = Console.ReadLine();
            if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            {
                ConsoleHost.Write("Aborted; nothing was changed.");
                return ExitCodes.Failure;
            }
        }

        using var backend = new BackendClient();
        if (await backend.SelectBackendAsync() is null)
        {
            ConsoleHost.Error("backend offline — please try again later.");
            return ExitCodes.BackendOffline;
        }

        var service = new SaveSwapService(backend);
        service.Progress += ConsoleHost.Write;

        var result = await service.SwapAsync(donor, target, xuid);
        foreach (var step in result.Steps)
            ConsoleHost.Write($"  [{(step.Ok ? "ok" : "!!")}] {step.Name}: {step.Detail}");

        if (!result.Success) { ConsoleHost.Error(result.Message); return ExitCodes.Failure; }
        ConsoleHost.Write(result.Message);
        return ExitCodes.Success;
    }

    private static int Restore(CliOptions o)
    {
        if (o.Positional.Count == 0) { ConsoleHost.Error("restore needs the save path. Usage: restore <C_ProfileData path>"); return ExitCodes.BadUsage; }
        var target = Path.GetFullPath(o.Positional[0]);

        using var backend = new BackendClient();
        var service = new SaveSwapService(backend);
        var result = service.RestoreOriginal(target);
        foreach (var step in result.Steps)
            ConsoleHost.Write($"  [{(step.Ok ? "ok" : "!!")}] {step.Name}: {step.Detail}");

        if (!result.Success) { ConsoleHost.Error(result.Message); return ExitCodes.Failure; }
        ConsoleHost.Write(result.Message);
        return ExitCodes.Success;
    }

    private static int ListSaves()
    {
        var candidates = SaveLocator.FindCandidates();
        if (candidates.Count == 0)
        {
            ConsoleHost.Write("No FH6 saves found (looked in Xbox PGS and RUNE locations).");
            return ExitCodes.Success;
        }
        foreach (var c in candidates)
        {
            ConsoleHost.Write($"[{c.Source}] {c.SizeLabel}  modified {c.Modified:yyyy-MM-dd HH:mm}");
            if (c.Xuid is not null) ConsoleHost.Write($"  xuid: {c.Xuid}");
            ConsoleHost.Write($"  {c.Path}");
        }
        return ExitCodes.Success;
    }

    private static int ProfileInspect(CliOptions o)
    {
        if (o.Positional.Count == 0) { ConsoleHost.Error("profile-inspect needs a decrypted FH6 ProfileData file."); return ExitCodes.BadUsage; }
        string path = Path.GetFullPath(o.Positional[0]);
        if (!File.Exists(path)) { ConsoleHost.Error($"file not found: {path}"); return ExitCodes.FileNotFound; }
        try
        {
            using var session = Fh6ProfileEditorSession.Open(path);
            var document = session.Document;
            var roundTrip = session.VerifyRoundTrip();
            ConsoleHost.Write("Forza Horizon 6 ProfileData");
            ConsoleHost.Write($"  file          : {path}");
            ConsoleHost.Write($"  encoding      : {(document.WasCompressed ? "zlib envelope" : "uncompressed sections")}");
            ConsoleHost.Write($"  size          : {document.OriginalSize:N0} bytes ({document.InflatedSize:N0} inflated)");
            ConsoleHost.Write($"  properties    : {roundTrip.Properties:N0}");
            ConsoleHost.Write($"  BXML nodes    : {roundTrip.BxmlNodes:N0} (version {document.Bxml.Version})");
            ConsoleHost.Write($"  binary records: {roundTrip.BinaryRecords:N0}");
            ConsoleHost.Write($"  XUID          : {document.Binary.Xuid} (0x{document.Binary.Xuid:X16})");
            ConsoleHost.Write($"  SQLite        : {roundTrip.SqliteIntegrity}");
            ConsoleHost.Write($"  lossless read : {(roundTrip.ByteIdentical ? "yes" : "no (normalized serialization)")}");
            return ExitCodes.Success;
        }
        catch (Exception ex) { ConsoleHost.Error(ex.Message); return ExitCodes.Unsupported; }
    }

    private static int ProfileSet(CliOptions o)
    {
        if (o.Positional.Count == 0 || o.Get("property", "p") is not string property || o.Get("value") is not string value)
        { ConsoleHost.Error("Usage: profile-set <decrypted> --property <path> --value <value> [-o output]"); return ExitCodes.BadUsage; }
        return EditProfile(o, session => session.UpdateProperty(property, value), "property");
    }

    private static int ProfileXuid(CliOptions o)
    {
        if (o.Positional.Count == 0 || o.Get("xuid", "x") is not string xuid)
        { ConsoleHost.Error("Usage: profile-xuid <decrypted> --xuid <id> [-o output]"); return ExitCodes.BadUsage; }
        return EditProfile(o, session => session.UpdateXuid(xuid), "XUID");
    }

    private static int ProfileSql(CliOptions o)
    {
        if (o.Positional.Count == 0 || o.Get("sql") is not string sql)
        { ConsoleHost.Error("Usage: profile-sql <decrypted> --sql <statement> [-o output]"); return ExitCodes.BadUsage; }
        return EditProfile(o, session =>
        {
            var result = session.ExecuteSql(sql);
            ConsoleHost.Write($"SQL completed: {result.Statements.Count} statement(s), {result.Changes:N0} row change(s).");
        }, "database");
    }

    private static int ProfileBinary(CliOptions o)
    {
        if (o.Positional.Count == 0 || o.Get("record", "r") is not string recordText
            || o.Get("offset") is not string offsetText || o.Get("type") is not string typeText
            || o.Get("value") is not string value)
        { ConsoleHost.Error("Usage: profile-binary <decrypted> --record <ordinal> --offset <n|0xN> --type <scalar> --value <value> [-o output]"); return ExitCodes.BadUsage; }
        if (!int.TryParse(recordText, out int record)) { ConsoleHost.Error("--record must be a numeric record ordinal."); return ExitCodes.BadUsage; }
        int offset;
        try { offset = offsetText.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt32(offsetText[2..], 16) : int.Parse(offsetText); }
        catch { ConsoleHost.Error("--offset must be decimal or 0x-prefixed hexadecimal."); return ExitCodes.BadUsage; }
        if (!Enum.TryParse<BinaryScalarType>(typeText, true, out var type))
        { ConsoleHost.Error("--type must be UInt8, Int8, UInt16, Int16, UInt32, Int32, UInt64, Int64, Float32, Float64, Bool8, or Bool32."); return ExitCodes.BadUsage; }
        return EditProfile(o, session => session.UpdateBinaryScalar(record, offset, type, value), "binary scalar");
    }

    private static int ProfileBxmlString(CliOptions o)
    {
        if (o.Positional.Count == 0 || o.Get("index", "i") is not string indexText || o.Get("value") is not string value
            || !int.TryParse(indexText, out int index))
        { ConsoleHost.Error("Usage: profile-bxml-string <decrypted> --index <n> --value <text> [-o output]"); return ExitCodes.BadUsage; }
        return EditProfile(o, session =>
        {
            if (index < 0 || index >= session.Document.Bxml.Strings.Count) throw new ArgumentOutOfRangeException(nameof(index));
            session.Document.Bxml.Strings[index] = value; session.BxmlDirty = true;
        }, "BXML string");
    }

    private static int EditProfile(CliOptions o, Action<Fh6ProfileEditorSession> edit, string label)
    {
        string input = Path.GetFullPath(o.Positional[0]);
        if (!File.Exists(input)) { ConsoleHost.Error($"file not found: {input}"); return ExitCodes.FileNotFound; }
        string output = ResolveOutput(o, input, Path.GetFileNameWithoutExtension(input) + "_edited" + Path.GetExtension(input));
        if (!ConfirmOverwrite(output, o)) return ExitCodes.Failure;
        try
        {
            using var session = Fh6ProfileEditorSession.Open(input);
            edit(session);
            var result = session.Save(output);
            ConsoleHost.Write($"FH6 {label} edit saved and verified ({result.Size:N0} bytes, SQLite {result.SqliteIntegrity}).");
            ConsoleHost.Write(result.Path);
            return ExitCodes.Success;
        }
        catch (Exception ex) { ConsoleHost.Error(ex.Message); return ExitCodes.Failure; }
    }

    private static string ResolveOutput(CliOptions o, string input, string defaultName)
    {
        var explicitOut = o.Get("output", "o");
        if (explicitOut is null)
            return Path.Combine(Path.GetDirectoryName(input)!, defaultName);

        var full = Path.GetFullPath(explicitOut);
        if (Directory.Exists(full) || explicitOut.EndsWith('\\') || explicitOut.EndsWith('/'))
            return Path.Combine(full, defaultName);
        return full;
    }

    private static bool ConfirmOverwrite(string output, CliOptions o)
    {
        if (!File.Exists(output) || o.Has("force", "f", "yes", "y")) return true;
        ConsoleHost.Error($"{output} already exists. Pass --force to overwrite.");
        return false;
    }

    public static void PrintHelp()
    {
        var exe = OperatingSystem.IsWindows() ? "forzacrypto" : "./forzacrypto";
        var help = new StringBuilder();
        help.AppendLine($"Forza Crypto Tool {BuildConfig.VersionLabel}");
        help.AppendLine();
        help.AppendLine("USAGE");
        help.AppendLine($"  {exe} <command> [options]");
        help.AppendLine();
        help.AppendLine("COMMANDS");
        help.AppendLine("  decrypt  <file>      Decrypt a GameDB (.slt), profile save, Method 22 ZIP or config file");
        help.AppendLine("  encrypt  <file>      Re-encrypt an edited file back into a game-loadable one");
        help.AppendLine("  detect   <file...>   Identify files and show what can be done with them");
        help.AppendLine("  saveswap <donor>     Swap a donor save into your active save slot");
        help.AppendLine("  restore  <save>      Undo a save swap from the tool's backups");
        help.AppendLine("  saves                List detected FH6 saves (Xbox PGS, RUNE, Proton/Wine)");
        help.AppendLine("  profile-inspect <f>  Validate and summarize decrypted FH6 ProfileData");
        help.AppendLine("  profile-set <f>      Edit a typed FH6 property (--property, --value)");
        help.AppendLine("  profile-xuid <f>     Edit the FH6 account XUID (--xuid)");
        help.AppendLine("  profile-sql <f>      Run SQL on the embedded career DB (--sql)");
        help.AppendLine("  profile-binary <f>   Patch a known fixed-width binary payload scalar");
        help.AppendLine("  profile-bxml-string  Replace one FH6 BXML string-table entry");
        help.AppendLine("  version, help");
        help.AppendLine();
        help.AppendLine("OPTIONS");
        help.AppendLine("  -o, --output <path>  Output file, or a directory to use the default name");
        help.AppendLine("      --original <f>   The original ENCRYPTED file (required to re-encrypt configs / Method 22)");
        help.AppendLine("      --target <path>  Save-swap destination (a C_ProfileData path)");
        help.AppendLine("      --rune           Target the RUNE save; its XUID is filled in automatically");
        help.AppendLine("  -x, --xuid <id>      Target account XUID (decimal or 0x hex)");
        help.AppendLine("  -f, --force          Overwrite an existing output file");
        help.AppendLine("  -y, --yes            Skip confirmation prompts");
        help.AppendLine();
        help.AppendLine("EXAMPLES");
        help.AppendLine($"  {exe} decrypt gamedbRC.slt -o db.sqlite");
        help.AppendLine($"  {exe} encrypt db.sqlite -o gamedbRC.slt --force");
        help.AppendLine($"  {exe} decrypt PhysicsSettings.ini");
        help.AppendLine($"  {exe} encrypt PhysicsSettings_decrypted.ini --original PhysicsSettings.ini");
        help.AppendLine($"  {exe} saveswap donor_C_ProfileData --rune --yes");
        help.AppendLine($"  {exe} saveswap donor_C_ProfileData --xuid 2535437902562438");
        help.AppendLine();
        help.AppendLine("EXIT CODES");
        help.AppendLine("  0 ok   1 failed   2 bad usage   3 file not found   4 backend offline   5 unsupported type");
        ConsoleHost.Write(help.ToString());
    }
}

internal sealed class CliOptions
{
    public List<string> Positional { get; } = new();
    private readonly Dictionary<string, string?> _named = new(StringComparer.OrdinalIgnoreCase);

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith('-')) { o.Positional.Add(a); continue; }

            var name = a.TrimStart('-');
            var eq = name.IndexOf('=');
            if (eq > 0)
            {
                o._named[name[..eq]] = name[(eq + 1)..];
                continue;
            }

            if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
            {
                o._named[name] = args[++i];
            }
            else
            {
                o._named[name] = null;
            }
        }
        return o;
    }

    public string? Get(params string[] names)
    {
        foreach (var n in names)
            if (_named.TryGetValue(n, out var v) && !string.IsNullOrWhiteSpace(v))
                return v;
        return null;
    }

    public bool Has(params string[] names) => names.Any(_named.ContainsKey);
}
