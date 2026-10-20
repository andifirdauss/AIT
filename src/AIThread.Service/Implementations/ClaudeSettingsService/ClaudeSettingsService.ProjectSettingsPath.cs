namespace AIThread.Service.Implementations;

public sealed partial class ClaudeSettingsService
{
    private static string ProjectSettingsPath(string rootPath) => Path.Combine(rootPath, ".claude", "settings.local.json");
}
