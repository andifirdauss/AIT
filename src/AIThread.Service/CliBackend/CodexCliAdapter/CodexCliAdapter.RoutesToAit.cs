namespace AIThread.Service.CliBackend;

public sealed partial class CodexCliAdapter
{
    // Whether the user's config.toml makes plain `codex` go through AIT: a top-level profile or model_provider set to
    // the provider the CLI menu adds. Then the user config is skipped even when IgnoreUserConfig is off, or every call
    // would come back here. Only those two keys are read.
    public static bool RoutesToAit(string configToml)
    {
        foreach (var raw in configToml.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) break;
            if (TopLevelKey().Match(line) is { Success: true } match
                && string.Equals(match.Groups["value"].Value, AitProvider, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool RoutesToAit()
    {
        var home = Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        try
        {
            var file = Path.Combine(home, "config.toml");
            return File.Exists(file) && RoutesToAit(File.ReadAllText(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
