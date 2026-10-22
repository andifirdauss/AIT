namespace AIThread.Service.Verification;

public sealed partial class BuildRunner
{
    // English messages without colours or banners. MSBuild variables inherited from whatever started AIThread (an IDE,
    // dotnet run) could point the build at another SDK, so they are dropped.
    internal static void PrepareEnvironment(IDictionary<string, string?> environment)
    {
        foreach (var key in environment.Keys.Where(k => k.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)).ToList())
            environment.Remove(key);

        environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        environment["VSLANG"] = "1033";
        environment["DOTNET_NOLOGO"] = "1";
        environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        environment["MSBUILDTERMINALLOGGER"] = "off";
        environment["NO_COLOR"] = "1";
        environment["FORCE_COLOR"] = "0";
        environment["CARGO_TERM_COLOR"] = "never";
        environment["npm_config_color"] = "false";
        environment["npm_config_update_notifier"] = "false";
        environment["npm_config_fund"] = "false";
    }
}
