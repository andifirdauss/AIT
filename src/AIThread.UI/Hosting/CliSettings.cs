using System.Text.Json;
using AIThread.Common.Localization;

namespace AIThread.UI.Hosting;

// Choices made in the exe's menu that must survive a restart. Infrastructure settings stay in appsettings.json.
public sealed class CliSettings
{
    public int Port { get; set; } = AppPaths.DefaultPort;

    // Language of the menu (Lang), kept in step with Setting → language by the server (ServerHost), so the menu speaks
    // it even while the server is not running.
    public string Language { get; set; } = Lang.Indonesian;

    public static void SaveLanguage(string language)
    {
        try
        {
            var settings = Load();
            if (settings.Language == language) return;
            settings.Language = language;
            settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public static CliSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.CliSettingsFile))
            {
                var loaded = JsonSerializer.Deserialize<CliSettings>(File.ReadAllText(AppPaths.CliSettingsFile));
                if (loaded is { Port: > 0 and <= 65535 }) return loaded;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new CliSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.HomeDir);
        File.WriteAllText(AppPaths.CliSettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
