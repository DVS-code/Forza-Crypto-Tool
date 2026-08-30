using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForzaCryptoTool;

internal sealed class AppSettings
{
    public string OutputFolder { get; set; } = DefaultOutputFolder;
    public List<string> RecentFiles { get; set; } = new();
    public bool CheckForUpdates { get; set; } = true;

    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    [JsonIgnore]
    public static string DefaultOutputFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        BuildConfig.AppName, "Output");

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), BuildConfig.AppName);
    private static string File_ => Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(File_))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(File_)) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Settings unreadable, using defaults: {ex.Message}");
        }
        return new AppSettings();
    }

    public void Save()
    {
        string? tempPath = null;
        try
        {
            Directory.CreateDirectory(Dir);
            tempPath = Path.Combine(Dir, $"settings.{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(JsonSerializer.Serialize(this, JsonOpts));
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, File_, overwrite: true);
            tempPath = null;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not save settings: {ex.Message}");
        }
        finally
        {
            if (tempPath is not null)
            {
                try { File.Delete(tempPath); }
                catch { }
            }
        }
    }

    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        while (RecentFiles.Count > 10) RecentFiles.RemoveAt(RecentFiles.Count - 1);
        Save();
    }

    public string ResolveOutputFolder()
    {
        var folder = string.IsNullOrWhiteSpace(OutputFolder) ? DefaultOutputFolder : OutputFolder;
        try
        {
            Directory.CreateDirectory(folder);
            return folder;
        }
        catch
        {
            Directory.CreateDirectory(DefaultOutputFolder);
            return DefaultOutputFolder;
        }
    }
}
