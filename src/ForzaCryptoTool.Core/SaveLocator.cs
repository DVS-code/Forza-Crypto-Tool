namespace ForzaCryptoTool;

internal static class SaveLocator
{
    public enum SaveFlavour { Pgs, Rune, Proton, Fh5 }

    public sealed record Candidate(
        string Path,
        string Source,
        DateTime Modified,
        long Size,
        SaveFlavour Flavour,
        string? Xuid = null,
        bool Recommended = false)
    {
        public string SizeLabel => Size >= 1024 * 1024
            ? $"{Size / (1024.0 * 1024.0):0.0} MB"
            : $"{Size / 1024.0:0.0} KB";

        public string ContainerDir => System.IO.Path.GetDirectoryName(Path) ?? "";

        public bool IsFh5 => Flavour == SaveFlavour.Fh5;
    }

    public static List<Candidate> FindCandidates()
    {
        var found = new List<Candidate>();
        if (OperatingSystem.IsWindows())
        {
            TryAdd(found, ScanPgs);
            TryAdd(found, ScanRune);
            TryAdd(found, ScanFh5Steam);
            TryAdd(found, ScanFh5OnlineFix);
            TryAdd(found, ScanFh5MsStore);
        }
        else
        {
            TryAdd(found, ScanWinePrefixes);
        }
        return found
            .GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(c => c.Recommended)
            .ThenByDescending(c => c.Modified)
            .ToList();
    }

    public static Candidate? FindNewest() => FindCandidates().FirstOrDefault();

    private static void TryAdd(List<Candidate> list, Func<IEnumerable<Candidate>> scan)
    {
        try { list.AddRange(scan()); }
        catch (Exception ex) { Logger.Detail($"Save scan source failed: {ex.Message}"); }
    }

    private static IEnumerable<Candidate> ScanPgs()
    {
        foreach (var root in PgsRoots())
            foreach (var candidate in ScanPgsRoot(root))
                yield return candidate;
    }

    private static IEnumerable<string> PgsRoots()
    {
        yield return @"C:\XboxGames\GameSave\pgs";
        foreach (var drive in SafeDrives())
        {
            var root = Path.Combine(drive.RootDirectory.FullName, "XboxGames", "GameSave", "pgs");
            if (!root.Equals(@"C:\XboxGames\GameSave\pgs", StringComparison.OrdinalIgnoreCase))
                yield return root;
        }
    }

    private static IEnumerable<Candidate> ScanPgsRoot(string pgsRoot)
    {
        if (!Directory.Exists(pgsRoot)) yield break;

        foreach (var accountRoot in SafeDirs(pgsRoot))
        {
            var accountName = Path.GetFileName(accountRoot);
            if (!accountName.StartsWith("u_", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = accountName.Split('_');
            var xuid = parts.Length >= 2 && parts[1].All(char.IsDigit) ? parts[1] : null;

            var versionDir = ResolveVersionDir(accountRoot);
            if (versionDir is null) continue;

            foreach (var c in ScanContainersRoot(
                Path.Combine(versionDir, "ContainersRoot"), "Xbox / retail (PGS)", SaveFlavour.Pgs, xuid))
                yield return c;
        }
    }

    private static string? ResolveVersionDir(string accountRoot)
    {
        var current = Path.Combine(accountRoot, "current");
        if (Directory.Exists(current)) return current;

        return SafeDirs(accountRoot)
            .Select(d => new { Path = d, Name = Path.GetFileName(d) })
            .Where(v => int.TryParse(v.Name, out _))
            .OrderByDescending(v => int.Parse(v.Name))
            .Select(v => v.Path)
            .FirstOrDefault();
    }

    private static IEnumerable<Candidate> ScanRune()
    {
        foreach (var root in RuneRoots())
            foreach (var c in ScanRuneRoot(root, "RUNE", SaveFlavour.Rune))
                yield return c;
    }

    private static IEnumerable<Candidate> ScanRuneRoot(string root, string source, SaveFlavour flavour)
    {
        if (!Directory.Exists(root)) yield break;

        foreach (var titleDir in SafeDirs(root))
        {
            if (!Path.GetFileName(titleDir).StartsWith("Forza Horizon 6", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var accountDir in SafeDirs(titleDir))
            {
                var xuid = Path.GetFileName(accountDir);
                if (!xuid.All(char.IsDigit)) continue;

                var containers = Path.Combine(accountDir, "SaveGames", "ContainersRoot");
                foreach (var c in ScanContainersRoot(containers, source, flavour, xuid))
                    yield return c;
            }
        }
    }

    private static IEnumerable<string> RuneRoots()
    {
        yield return RuneProfile.StoreRoot;
        foreach (var drive in SafeDrives())
        {
            var root = Path.Combine(drive.RootDirectory.FullName, "Users", "Public", "Documents", "MicrosoftStore", "RUNE");
            if (!root.Equals(RuneProfile.StoreRoot, StringComparison.OrdinalIgnoreCase))
                yield return root;
        }
    }

    private const string Fh5SteamAppId = "1551360";

    private static IEnumerable<Candidate> ScanFh5Steam()
    {
        foreach (var userdata in SteamUserdataRoots())
            foreach (var steamAccount in SafeDirs(userdata))
                foreach (var xuidDir in SafeDirs(Path.Combine(steamAccount, Fh5SteamAppId, "remote")))
                {
                    var name = Path.GetFileName(xuidDir);
                    var xuid = name.Length > 0 && name.All(char.IsDigit) ? name : null;
                    foreach (var save in SafeFiles(xuidDir, "*.ProfileData"))
                        yield return Fh5Candidate(save, "FH5 / Steam", xuid);
                }
    }

    private static IEnumerable<string> SteamUserdataRoots()
    {
        var roots = new List<string> { @"C:\Program Files (x86)\Steam" };
        if (OperatingSystem.IsWindows())
        {
            foreach (var (hive, key, value) in new[]
            {
                (Microsoft.Win32.Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
            })
            {
                try
                {
                    if (hive.OpenSubKey(key)?.GetValue(value) is string path && path.Length > 0)
                        roots.Add(path.Replace('/', '\\'));
                }
                catch { }
            }
        }
        return roots
            .Select(root => Path.Combine(root, "userdata"))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<Candidate> ScanFh5OnlineFix()
    {
        var root = $@"C:\Users\Public\Documents\OnlineFix\{Fh5SteamAppId}\Saves";
        foreach (var folder in SafeDirs(root))
            foreach (var save in SafeFiles(folder, "*.ProfileData"))
                yield return Fh5Candidate(save, "FH5 / OnlineFix", null);
    }

    private static IEnumerable<Candidate> ScanFh5MsStore()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var package in new[] { "Microsoft.624F8B84B80_8wekyb3d8bbwe", "Microsoft.SunriseBaseGame_8wekyb3d8bbwe" })
        {
            var wgs = Path.Combine(localAppData, "Packages", package, "SystemAppData", "wgs");
            foreach (var account in SafeDirs(wgs))
            {
                if (Path.GetFileName(account).Equals("t", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var container in SafeDirs(account))
                {
                    var files = SafeFiles(container, "*").ToList();
                    bool holdsProfile = files
                        .Where(f => Path.GetFileName(f).StartsWith("container.", StringComparison.OrdinalIgnoreCase))
                        .Any(f => ContainsAscii(f, "ProfileData"));
                    if (!holdsProfile) continue;

                    var save = files.OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
                    if (save is not null)
                        yield return Fh5Candidate(save, "FH5 / Microsoft Store", null);
                }
            }
        }
    }

    private static Candidate Fh5Candidate(string path, string source, string? xuid)
    {
        var info = new FileInfo(path);
        return new Candidate(info.FullName, source, info.LastWriteTime, info.Length, SaveFlavour.Fh5, xuid);
    }

    private static bool ContainsAscii(string path, string text)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[Math.Min(stream.Length, 1 << 20)];
            int read = stream.Read(buffer, 0, buffer.Length);
            return buffer.AsSpan(0, read).IndexOf(System.Text.Encoding.ASCII.GetBytes(text)) >= 0;
        }
        catch { return false; }
    }

    private static IEnumerable<Candidate> ScanWinePrefixes()
    {
        foreach (var driveC in WineDriveCRoots())
        {
            var runeRoot = Path.Combine(driveC, "users", "Public", "Documents", "MicrosoftStore", "RUNE");
            foreach (var c in ScanRuneRoot(runeRoot, "RUNE (Proton/Wine)", SaveFlavour.Proton))
                yield return c;

            foreach (var c in ScanPgsRoot(Path.Combine(driveC, "XboxGames", "GameSave", "pgs")))
                yield return c with { Source = "Xbox PGS (Proton/Wine)", Flavour = SaveFlavour.Proton };
        }
    }

    private static IEnumerable<string> WineDriveCRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home)) yield break;

        var steamRoots = new[]
        {
            Path.Combine(home, ".steam", "steam", "steamapps", "compatdata"),
            Path.Combine(home, ".local", "share", "Steam", "steamapps", "compatdata"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam", "steamapps", "compatdata"),
        };
        foreach (var root in steamRoots)
            foreach (var app in SafeDirs(root))
            {
                var drive = Path.Combine(app, "pfx", "drive_c");
                if (Directory.Exists(drive)) yield return drive;
            }

        var plainPrefixes = new[]
        {
            Path.Combine(home, ".wine"),
            Path.Combine(home, "Games"),
            Path.Combine(home, ".local", "share", "lutris", "prefixes"),
        };
        foreach (var root in plainPrefixes)
        {
            var direct = Path.Combine(root, "drive_c");
            if (Directory.Exists(direct)) yield return direct;
            foreach (var sub in SafeDirs(root))
            {
                var drive = Path.Combine(sub, "drive_c");
                if (Directory.Exists(drive)) yield return drive;
            }
        }
    }

    private static IEnumerable<Candidate> ScanContainersRoot(
        string containersRoot, string source, SaveFlavour flavour, string? xuid)
    {
        if (!Directory.Exists(containersRoot)) yield break;

        foreach (var userDir in SafeDirs(containersRoot))
        {
            var userName = Path.GetFileName(userDir);

            if (!userName.StartsWith("User_", StringComparison.OrdinalIgnoreCase)
                || userName.EndsWith("_Backup", StringComparison.OrdinalIgnoreCase))
                continue;

            var profile = Path.Combine(userDir, "C_ProfileData");
            if (!File.Exists(profile)) continue;

            var fi = new FileInfo(profile);
            if (fi.Length > 0)
                yield return new Candidate(fi.FullName, source, fi.LastWriteTime, fi.Length, flavour, xuid, Recommended: true);
        }
    }

    private static IEnumerable<DriveInfo> SafeDrives()
    {
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch { yield break; }
        foreach (var d in drives)
        {
            bool ok;
            try { ok = d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable; }
            catch { ok = false; }
            if (ok) yield return d;
        }
    }

    private static IEnumerable<string> SafeDirs(string path)
    {
        try { return Directory.Exists(path) ? Directory.GetDirectories(path) : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeFiles(string path, string pattern)
    {
        try { return Directory.Exists(path) ? Directory.GetFiles(path, pattern) : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }
}

internal static class RuneProfile
{
    public const string XuidDecimal = "1337133713371337";

    public const string XuidHex = "0004C01DB400B0C9";

    public const ulong Xuid = 1337133713371337UL;

    public const string ContainerFolder = "User_4C01DB400B0C9";

    public static readonly string StoreRoot =
        @"C:\Users\Public\Documents\MicrosoftStore\RUNE";

    public static bool IsRunePath(string path) =>
        path.Contains(@"\MicrosoftStore\RUNE\", StringComparison.OrdinalIgnoreCase)
        || path.Contains(ContainerFolder, StringComparison.OrdinalIgnoreCase);

    public static bool SelfCheck() => Convert.ToUInt64(XuidHex, 16) == Xuid;
}
