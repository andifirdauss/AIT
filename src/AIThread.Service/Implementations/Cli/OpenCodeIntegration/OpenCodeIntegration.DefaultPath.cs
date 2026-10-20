namespace AIThread.Service.Implementations.Cli;

public sealed partial class OpenCodeIntegration
{
    // $XDG_CONFIG_HOME/opencode (default ~/.config/opencode, also on Windows); an existing opencode.jsonc is used as is.
    private static string DefaultPath()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg ? xdg : Path.Combine(UserProfile, ".config");
        var directory = Path.Combine(configHome, "opencode");
        var json = Path.Combine(directory, "opencode.json");
        var jsonc = Path.Combine(directory, "opencode.jsonc");
        return !File.Exists(json) && File.Exists(jsonc) ? jsonc : json;
    }
}
