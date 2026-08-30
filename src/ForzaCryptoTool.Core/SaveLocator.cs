namespace ForzaCryptoTool;

internal static class SaveLocator
{
    public enum SaveFlavour { Pgs, Rune, Proton }

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
    }

    public static List<Candidate> FindCandidates()
    {
        var found = new List<Candidate>();
        if (OperatingSystem.IsWindows())
        {
            TryAdd(found, ScanPgs);
            TryAdd(found, ScanRune);
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
        try { return Directory.EnumerateDirectories(path); }
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
