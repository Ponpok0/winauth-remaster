using System.IO;
using System.Text.Json;
using WinAuthRemaster.Models;

namespace WinAuthRemaster.Services;

public sealed class SettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinAuth");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new AppSettings();

        try
        {
            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        if (!Directory.Exists(SettingsDir))
            Directory.CreateDirectory(SettingsDir);

        string json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }

    public string GetConfigFilePath(AppSettings settings)
    {
        return settings.ConfigFilePath
            ?? Path.Combine(SettingsDir, "winauth.json");
    }
}
