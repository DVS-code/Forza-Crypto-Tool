using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace ForzaCryptoTool;

internal static class XuidGrabber
{
    internal sealed record XboxAccount(ulong Xuid, string Gamertag);

    private const double ScanTimeLimitSeconds = 6.0;
    private const long ScanMaxBytes = 256L * 1024 * 1024;

    private static readonly string[] CandidateProcesses =
    {
        "xboxpcapp", "xboxpcappft", "gamingservices", "gamingservicesnet", "gamingapp",
        "microsoft.gamingapp", "xboxidentityprovider", "applicationframehost",
        "xboxappservices", "xboxgamebar", "xboxgamebarft", "xboxgamebarftserver",
    };

    public static void LaunchXboxApp()
    {
        try
        {
            Process.Start(new ProcessStartInfo("xbox://") { UseShellExecute = true });
        }
        catch
        {
            Process.Start(new ProcessStartInfo("explorer.exe",
                "shell:AppsFolder\\Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App")
            { UseShellExecute = true });
        }
    }

    public static bool IsElevated()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public static bool RelaunchAsAdmin()
    {
        try
        {
            var exe = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("Could not determine the executable path.");
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<ulong> GrabAsync(Action<string>? status = null)
        => (await GrabAccountAsync(status)).Xuid;

    public static async Task<XboxAccount> GrabAccountAsync(Action<string>? status = null)
    {
        var (_, account) = await AuthenticateAsync(status);
        return account;
    }

    public static async Task<IReadOnlyDictionary<ulong, string>> ResolveAccountsAsync(
        IEnumerable<ulong> xuids, Action<string>? status = null)
    {
        var requested = xuids.Where(xuid => xuid != 0).Distinct().ToArray();
        var (xauth, signedIn) = await AuthenticateAsync(status);
        var result = new Dictionary<ulong, string> { [signedIn.Xuid] = signedIn.Gamertag };
        using var http = CreateXboxHttpClient();
        foreach (ulong xuid in requested.Where(value => value != signedIn.Xuid))
        {
            status?.Invoke($"Looking up Xbox account {xuid}…");
            string? gamertag = await FetchGamertagAsync(http, xauth, xuid);
            if (!string.IsNullOrWhiteSpace(gamertag)) result[xuid] = gamertag;
        }
        return result;
    }

    private static async Task<(string Token, XboxAccount Account)> AuthenticateAsync(Action<string>? status)
    {
        EnableDebugPrivilege();
        var candidates = FindCandidatePids();
        if (candidates.Count == 0)
            throw new InvalidOperationException("Xbox App is not running. Open it, sign in, then try again.");

        bool openedAny = false;
        bool tokenFound = false;

        foreach (var (name, pid) in candidates)
        {
            status?.Invoke($"Scanning {name}…");
            Logger.Info($"Grab: scanning {name} (pid {pid}).");
            var xauth = ScanXauth(pid, out var opened);
            openedAny |= opened;
            if (xauth is null)
                continue;

            tokenFound = true;
            status?.Invoke("Resolving XUID from Xbox Live…");
            var account = await FetchSignedInAccountAsync(xauth);
            if (account is not null)
            {
                Logger.Success("XUID resolved from the signed-in Xbox account.");
                return (xauth, account);
            }
        }

        if (!openedAny)
        {
            Logger.Warn($"Grab: could not open any Xbox process (elevated={IsElevated()}).");
            throw new InvalidOperationException(IsElevated()
                ? "Could not read the Xbox App's memory even as Administrator. Make sure the Xbox App is running and signed in."
                : "Grab XUID needs Administrator access to read the Xbox App's memory. Relaunch ForzaCryptoTool as Administrator and try again.");
        }
        if (tokenFound)
            throw new InvalidOperationException(
                "Found the Xbox auth token but Xbox Live didn't return a XUID. Make sure you're signed in and online.");

        throw new InvalidOperationException(
            "Could not find the Xbox auth token. Open the Xbox App, sign in to the target account, then try again.");
    }

    private static HttpClient CreateXboxHttpClient() => new() { Timeout = TimeSpan.FromSeconds(10) };

    private static async Task<XboxAccount?> FetchSignedInAccountAsync(string xauth)
    {
        try
        {
            using var http = CreateXboxHttpClient();
            using var req = new HttpRequestMessage(HttpMethod.Get,
                "https://profile.xboxlive.com/users/me/profile/settings?settings=Gamertag");
            req.Headers.TryAddWithoutValidation("Authorization", xauth);
            req.Headers.TryAddWithoutValidation("x-xbl-contract-version", "2");
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("profileUsers", out var users) || users.GetArrayLength() == 0)
                return null;
            var user = users[0];
            if (!user.TryGetProperty("id", out var id) || !ulong.TryParse(id.GetString(), out ulong xuid))
                return null;
            string gamertag = "Xbox account";
            if (user.TryGetProperty("settings", out var settings))
                foreach (var setting in settings.EnumerateArray())
                    if (setting.TryGetProperty("id", out var key) && key.GetString() == "Gamertag"
                        && setting.TryGetProperty("value", out var value) && !string.IsNullOrWhiteSpace(value.GetString()))
                        gamertag = value.GetString()!;
            return new XboxAccount(xuid, gamertag);
        }
        catch (Exception ex)
        {
            Logger.Detail($"XUID lookup failed: {ex.Message}");
        }
        return null;
    }

    private static async Task<string?> FetchGamertagAsync(HttpClient http, string xauth, ulong xuid)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://peoplehub.xboxlive.com/users/me/people/xuids({xuid})");
            req.Headers.TryAddWithoutValidation("Authorization", xauth);
            req.Headers.TryAddWithoutValidation("x-xbl-contract-version", "1");
            req.Headers.TryAddWithoutValidation("x-xbl-client-type", "UWA");
            req.Headers.TryAddWithoutValidation("x-xbl-client-name", "XboxApp");
            req.Headers.TryAddWithoutValidation("Cache-Control", "no-store, no-cache");
            using var response = await http.SendAsync(req);
            if (!response.IsSuccessStatusCode) return null;
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!document.RootElement.TryGetProperty("people", out var people) || people.GetArrayLength() == 0)
                return null;
            return people[0].TryGetProperty("gamertag", out var gamertag) ? gamertag.GetString() : null;
        }
        catch (Exception ex)
        {
            Logger.Detail($"Gamertag lookup for XUID {xuid} failed: {ex.Message}");
            return null;
        }
    }

    private static string? ScanXauth(int pid, out bool opened)
    {
        const int access = PROCESS_QUERY_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ;
        var handle = OpenProcess(access, false, (uint)pid);
        opened = handle != IntPtr.Zero;
        if (handle == IntPtr.Zero)
            return null;

        var asciiPattern = "XBL3.0 x="u8.ToArray();
        var widePattern = Encoding.Unicode.GetBytes("XBL3.0 x=");
        var watch = Stopwatch.StartNew();
        long scanned = 0;
        try
        {
            foreach (var (baseAddr, size) in EnumerateRegions(handle))
            {
                long offset = 0;
                while (offset < size)
                {
                    if (watch.Elapsed.TotalSeconds >= ScanTimeLimitSeconds || scanned >= ScanMaxBytes)
                        return null;
                    int toRead = (int)Math.Min(1024 * 1024, size - offset);
                    var chunk = ReadBytes(handle, baseAddr + offset, toRead);
                    if (chunk is not null)
                    {
                        scanned += chunk.Length;
                        var token = FindToken(handle, chunk, baseAddr + offset, asciiPattern, false)
                                 ?? FindToken(handle, chunk, baseAddr + offset, widePattern, true);
                        if (token is not null)
                            return token;
                    }
                    offset += toRead;
                }
            }
        }
        finally
        {
            CloseHandle(handle);
        }
        return null;
    }

    private static string? FindToken(IntPtr handle, byte[] data, long baseAddr, byte[] pattern, bool wide)
    {
        int idx = IndexOf(data, pattern, 0);
        while (idx != -1)
        {
            var raw = ReadBytes(handle, baseAddr + idx, wide ? 20000 : 10000);
            if (raw is not null)
            {
                string text;
                if (wide)
                {
                    int term = IndexOf(raw, new byte[] { 0, 0 }, 0);
                    text = Encoding.Unicode.GetString(term >= 0 ? raw[..term] : raw);
                }
                else
                {
                    int term = Array.IndexOf(raw, (byte)0);
                    text = Encoding.UTF8.GetString(term >= 0 ? raw[..term] : raw);
                }
                var token = ExtractToken(text);
                if (token is not null)
                    return token;
            }
            idx = IndexOf(data, pattern, idx + 1);
        }
        return null;
    }

    private static string? ExtractToken(string text)
    {
        const string prefix = "xbl3.0 x=";
        int start = text.ToLowerInvariant().IndexOf(prefix, StringComparison.Ordinal);
        if (start == -1)
            return null;
        var tail = text[(start + prefix.Length)..];
        const string allowed = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/_=.-;";
        var sb = new StringBuilder("XBL3.0 x=");
        foreach (var ch in tail)
        {
            if (allowed.IndexOf(ch) >= 0) sb.Append(ch);
            else break;
        }
        var token = sb.ToString();
        return token.Contains(';') ? token : null;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start)
    {
        for (int i = start; i + needle.Length <= haystack.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
                if (haystack[i + j] != needle[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }

    private static IEnumerable<(long baseAddr, long size)> EnumerateRegions(IntPtr handle)
    {
        long address = 0;
        while (true)
        {
            if (VirtualQueryEx(handle, (IntPtr)address, out var mbi, (uint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>()) == 0)
                yield break;
            if (mbi.RegionSize == 0)
                yield break;
            bool committed = mbi.State == MEM_COMMIT
                && (mbi.Protect & PAGE_NOACCESS) == 0
                && (mbi.Protect & PAGE_GUARD) == 0;
            if (committed)
                yield return ((long)mbi.BaseAddress, (long)mbi.RegionSize);
            long next = (long)mbi.BaseAddress + (long)mbi.RegionSize;
            if (next <= address)
                yield break;
            address = next;
        }
    }

    private static byte[]? ReadBytes(IntPtr handle, long address, int length)
    {
        var buffer = new byte[length];
        if (!ReadProcessMemory(handle, (IntPtr)address, buffer, length, out var read) || read == 0)
            return null;
        return read == length ? buffer : buffer[..(int)read];
    }

    private static List<(string name, int pid)> FindCandidatePids()
    {
        var found = new List<(string, int)>();
        foreach (var name in CandidateProcesses)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                found.Add((name, p.Id));
                p.Dispose();
            }
        }

        return found
            .OrderBy(f => Array.IndexOf(CandidateProcesses, f.Item1))
            .ToList();
    }

    private static void EnableDebugPrivilege()
    {
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token))
                return;
            try
            {
                if (!LookupPrivilegeValue(null, "SeDebugPrivilege", out var luid))
                    return;
                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = SE_PRIVILEGE_ENABLED,
                };
                AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally { CloseHandle(token); }
        }
        catch {  }
    }

    private const int PROCESS_QUERY_INFORMATION = 0x0400;
    private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int PROCESS_VM_READ = 0x0010;
    private const uint MEM_COMMIT = 0x1000;
    private const uint PAGE_NOACCESS = 0x01;
    private const uint PAGE_GUARD = 0x100;
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x20;
    private const uint TOKEN_QUERY = 0x8;
    private const uint SE_PRIVILEGE_ENABLED = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORY_BASIC_INFORMATION
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public IntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID Luid; public uint Attributes; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, uint pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int VirtualQueryEx(IntPtr handle, IntPtr address, out MEMORY_BASIC_INFORMATION mbi, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr handle, IntPtr address, byte[] buffer, int size, out IntPtr read);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out LUID luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES newState, uint length, IntPtr prev, IntPtr prevLength);
}
