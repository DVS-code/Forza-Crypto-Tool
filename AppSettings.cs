using System.Text.Json;

namespace ForzaCryptoTool;

internal sealed class AppSettings
{

    public string OutputFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ForzaCryptoTool", "Output");
    public List<string> RecentFiles { get; set; } = new();

    public bool AutoUpdateCheck { get; set; } = true;

    public string LanguageCode { get; set; } = Localization.DefaultLanguageCode;

    public string? SkippedUpdateVersion { get; set; }

    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ForzaCryptoTool", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch
        {

        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {

        }
    }

    public void AddRecentFile(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 8)
            RecentFiles.RemoveRange(8, RecentFiles.Count - 8);
        Save();
    }
}
