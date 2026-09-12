using System.Text;
using System.Text.RegularExpressions;

namespace ForzaCryptoTool;

internal enum LogLevel { Detail, Info, Success, Warning, Error }

internal static class Logger
{
    public const int MaxFiles = 20;
    public const int MaxAgeDays = 14;

    private static readonly object Gate = new();
    private static StreamWriter? _writer;
    private static string? _path;

    public static string? CurrentLogPath => _path;

    public static Action<LogLevel, string>? UiSink { get; set; }

    public static string LogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), BuildConfig.AppName, "logs");

    public static void Initialize()
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                ApplyRetention();
                _path = Path.Combine(LogDirectory, $"{BuildConfig.AppName}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
                _writer = new StreamWriter(new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.Read), Encoding.UTF8)
                {
                    AutoFlush = true,
                };
                WriteRaw(LogLevel.Info, $"=== {BuildConfig.AppName} {BuildConfig.VersionLabel} started ===");
                WriteRaw(LogLevel.Detail, $"OS {Environment.OSVersion}  CLR {Environment.Version}");
            }
            catch
            {
                _writer = null;
            }
        }
    }

    public static void Shutdown()
    {
        lock (Gate)
        {
            try { WriteRaw(LogLevel.Info, "=== session ended ==="); _writer?.Flush(); _writer?.Dispose(); }
            catch {  }
            _writer = null;
        }
    }

    public static void Log(LogLevel level, string message)
    {
        if (!BuildConfig.VerboseLogging && level == LogLevel.Detail)
            return;
        var safe = Redact(message);
        lock (Gate)
            WriteRaw(level, safe);
    }

    public static void Detail(string m) => Log(LogLevel.Detail, m);
    public static void Info(string m) => Log(LogLevel.Info, m);
    public static void Success(string m) => Log(LogLevel.Success, m);
    public static void Warn(string m) => Log(LogLevel.Warning, m);
    public static void Error(string m) => Log(LogLevel.Error, m);

    public static void Exception(string context, Exception ex)
    {
        Log(LogLevel.Error, $"{context}: {Redact(ex.Message)}");
        if (BuildConfig.VerboseLogging)
            lock (Gate)
                WriteRaw(LogLevel.Detail, Redact(ex.ToString()));
    }

    private static void WriteRaw(LogLevel level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] [{level.ToString().ToUpperInvariant()}] {message}";
        try { _writer?.WriteLine(line); } catch {  }
        try { UiSink?.Invoke(level, message); } catch {  }
    }

    private static void ApplyRetention()
    {
        try
        {
            var files = new DirectoryInfo(LogDirectory).GetFiles($"{BuildConfig.AppName}_*.log");
            var cutoff = DateTime.Now.AddDays(-MaxAgeDays);
            foreach (var f in files)
            {
                if (f.LastWriteTime < cutoff)
                    TryDelete(f);
            }
            var remaining = new DirectoryInfo(LogDirectory).GetFiles($"{BuildConfig.AppName}_*.log");
            foreach (var f in remaining.OrderByDescending(f => f.LastWriteTime).Skip(MaxFiles))
                TryDelete(f);
        }
        catch {  }
    }

    private static void TryDelete(FileInfo f) { try { f.Delete(); } catch {  } }

    private static readonly Regex BearerRx = new(@"(?i)\bbearer\s+[A-Za-z0-9._\-]+", RegexOptions.Compiled);
    private static readonly Regex HostRx = new(@"https?://[^\s/]+", RegexOptions.Compiled);
    private static readonly Regex KeyRx = new(@"(?i)\b([A-Za-z0-9+/]{40,}={0,2})\b", RegexOptions.Compiled);
    private static readonly Regex UserPathRx = new(@"(?i)([A-Za-z]:\\Users\\)[^\\]+", RegexOptions.Compiled);

    public static string Redact(string message)
    {
        if (string.IsNullOrEmpty(message))
            return message;
        var s = BearerRx.Replace(message, "Bearer <redacted>");
        s = HostRx.Replace(s, m => BuildConfig.RevealEndpoint ? m.Value : "<endpoint>");
        s = KeyRx.Replace(s, "<redacted>");
        s = UserPathRx.Replace(s, "$1<user>");
        return s;
    }
}
