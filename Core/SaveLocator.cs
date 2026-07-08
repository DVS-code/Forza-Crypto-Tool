using System.Text;

namespace ForzaCryptoTool;

internal static class SaveLocator
{
    public sealed record Candidate(string Path, string Source, DateTime Modified, long Size, string ProfileKind = "settings", bool Recommended = false);

    private static readonly string[] ProfileMarkers = { "profilebackup", "c_profiledata", "profiledata" };

    public static List<Candidate> FindCandidates()
    {
        var found = new List<Candidate>();
        TryAdd(found, ScanPgs);
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
        if (!Directory.Exists(pgsRoot))
            yield break;

        foreach (var accountRoot in SafeDirs(pgsRoot))
        {
            var accountName = Path.GetFileName(accountRoot);
            if (!accountName.StartsWith("u_", StringComparison.OrdinalIgnoreCase))
                continue;

            var versionDir = ResolvePgsVersionDir(accountRoot);
            if (versionDir is null)
                continue;

            var containersRoot = Path.Combine(versionDir, "ContainersRoot");
            if (!Directory.Exists(containersRoot))
                continue;

            foreach (var userDir in SafeDirs(containersRoot))
            {
                var userName = Path.GetFileName(userDir);
                if (!userName.StartsWith("User_", StringComparison.OrdinalIgnoreCase)
                    || userName.EndsWith("_Backup", StringComparison.OrdinalIgnoreCase))
                    continue;

                var profile = Path.Combine(userDir, "C_ProfileData");
                if (!File.Exists(profile))
                    continue;

                var fi = new FileInfo(profile);
                if (fi.Length > 0)
                    yield return new Candidate(fi.FullName, "PGS gameplay", fi.LastWriteTime, fi.Length, "pgs", true);
            }
        }
    }

    private static string? ResolvePgsVersionDir(string accountRoot)
    {
        var current = Path.Combine(accountRoot, "current");
        if (Directory.Exists(current))
            return current;

        return SafeDirs(accountRoot)
            .Select(d => new { Path = d, Name = Path.GetFileName(d) })
            .Where(v => int.TryParse(v.Name, out _))
            .OrderByDescending(v => int.Parse(v.Name))
            .Select(v => v.Path)
            .FirstOrDefault();
    }

    private static IEnumerable<Candidate> ScanWgs()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var packagesRoot = Path.Combine(localAppData, "Packages");
        if (!Directory.Exists(packagesRoot))
            yield break;

        foreach (var pkg in SafeDirs(packagesRoot))
        {
            var wgs = Path.Combine(pkg, "SystemAppData", "wgs");
            if (!Directory.Exists(wgs))
                continue;
            foreach (var account in SafeDirs(wgs))
            {
                if (string.Equals(Path.GetFileName(account), "t", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var container in SafeDirs(account))
                {
                    if (!ContainerMentionsProfile(container))
                        continue;

                    var blob = SafeFiles(container)
                        .Where(f => !Path.GetFileName(f).StartsWith("container.", StringComparison.OrdinalIgnoreCase))
                        .Select(f => new FileInfo(f))
                        .Where(fi => fi.Length > 0)
                        .OrderByDescending(fi => fi.Length)
                        .FirstOrDefault();
                    if (blob is not null)
                        yield return new Candidate(blob.FullName, "MS Store / Xbox", blob.LastWriteTime, blob.Length);
                }
            }
        }
    }

    private static bool ContainerMentionsProfile(string containerDir)
    {
        foreach (var file in SafeFiles(containerDir))
        {
            if (!Path.GetFileName(file).StartsWith("container.", StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                var bytes = File.ReadAllBytes(file);
                var text = Encoding.Unicode.GetString(bytes).ToLowerInvariant();
                if (ProfileMarkers.Any(m => text.Contains(m)))
                    return true;
            }
            catch {  }
        }
        return false;
    }

    private static IEnumerable<Candidate> ScanOnlineFix()
    {
        var baseDir = @"C:\Users\Public\Documents\OnlineFix";
        if (!Directory.Exists(baseDir))
            yield break;
        foreach (var file in SafeWalk(baseDir))
        {
            var name = Path.GetFileName(file).ToLowerInvariant();
            if (ProfileMarkers.Any(m => name.Contains(m)) || name.EndsWith(".profiledata"))
            {
                var fi = new FileInfo(file);
                yield return new Candidate(fi.FullName, "OnlineFix", fi.LastWriteTime, fi.Length);
            }
        }
    }

    private static IEnumerable<Candidate> ScanLocalStorageShared()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var packagesRoot = Path.Combine(localAppData, "Packages");
        if (!Directory.Exists(packagesRoot))
            yield break;
        foreach (var pkg in SafeDirs(packagesRoot))
        {
            var shared = Path.Combine(pkg, "LocalState", "LocalStorage_Shared");
            if (!Directory.Exists(shared))
                continue;
            foreach (var file in SafeWalk(shared))
            {
                var name = Path.GetFileName(file).ToLowerInvariant();
                if (ProfileMarkers.Any(m => name.Contains(m)))
                {
                    var fi = new FileInfo(file);
                    yield return new Candidate(fi.FullName, "LocalStorage", fi.LastWriteTime, fi.Length);
                }
            }
        }
    }

    private static IEnumerable<Candidate> ScanStandaloneLocalStorage()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var root = Path.Combine(localAppData, "ForzaHorizon6");
        foreach (var candidate in ScanTreeForProfiles(root, "LocalStorage (standalone)"))
            yield return candidate;
    }

    private static IEnumerable<Candidate> ScanFixedDrives()
    {
        foreach (var drive in SafeDrives())
        {
            var r = drive.RootDirectory.FullName;
            var label = $"Drive {drive.Name.TrimEnd('\\')}";

            foreach (var small in new[] { Path.Combine(r, "OnlineFix"), Path.Combine(r, "ForzaHorizon6") })
                foreach (var c in ScanTreeForProfiles(small, label, maxDepth: 8))
                    yield return c;

            foreach (var game in GameDirCandidates(r))
            {
                foreach (var c in ScanTreeForProfiles(game, label, maxDepth: 1))
                    yield return c;
                foreach (var sub in new[] { "LocalStorage_Shared", "Saves", "SaveGames", "Save", "ConnectedStorage" })
                    foreach (var c in ScanTreeForProfiles(Path.Combine(game, sub), label, maxDepth: 5))
                        yield return c;
            }
        }
    }

    private static IEnumerable<string> GameDirCandidates(string driveRoot) => new[]
    {
        Path.Combine(driveRoot, "Forza Horizon 6"),
        Path.Combine(driveRoot, "Forza Horizon 6", "Forza Horizon 6"),
        Path.Combine(driveRoot, "Games", "Forza Horizon 6"),
        Path.Combine(driveRoot, "SteamLibrary", "steamapps", "common", "Forza Horizon 6"),
        Path.Combine(driveRoot, "SteamLibrary", "steamapps", "common", "ForzaHorizon6"),
        Path.Combine(driveRoot, "Steam", "steamapps", "common", "Forza Horizon 6"),
        Path.Combine(driveRoot, "Program Files (x86)", "Steam", "steamapps", "common", "Forza Horizon 6"),
    };

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
            if (ok)
                yield return d;
        }
    }

    private static IEnumerable<Candidate> ScanTreeForProfiles(string root, string source, int maxDepth = int.MaxValue)
    {
        if (!Directory.Exists(root))
            yield break;
        foreach (var file in SafeWalk(root, maxDepth))
        {

            if (file.Contains(@"\_", StringComparison.Ordinal))
                continue;

            if (Path.GetExtension(file).Length > 0)
                continue;
            var name = Path.GetFileName(file).ToLowerInvariant();
            if (!ProfileMarkers.Any(m => name.Contains(m)))
                continue;
            var fi = new FileInfo(file);
            if (fi.Length > 0)
                yield return new Candidate(fi.FullName, source, fi.LastWriteTime, fi.Length);
        }
    }

    private static IEnumerable<string> SafeDirs(string path)
    {
        try { return Directory.EnumerateDirectories(path); }
        catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeFiles(string path)
    {
        try { return Directory.EnumerateFiles(path); }
        catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeWalk(string root, int maxDepth = int.MaxValue)
    {
        var stack = new Stack<(string Dir, int Depth)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            var (dir, depth) = stack.Pop();
            if (depth < maxDepth)
                foreach (var sub in SafeDirs(dir))
                    stack.Push((sub, depth + 1));
            foreach (var file in SafeFiles(dir))
                yield return file;
        }
    }
}
