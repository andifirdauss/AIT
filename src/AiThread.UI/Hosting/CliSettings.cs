using System.Text.Json;

namespace AiThread.UI.Hosting;

// Choices made in the exe's menu that must survive a restart. Infrastructure settings stay in appsettings.json.
public sealed class CliSettings
{
    public int Port { get; set; } = AppPaths.DefaultPort;

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
