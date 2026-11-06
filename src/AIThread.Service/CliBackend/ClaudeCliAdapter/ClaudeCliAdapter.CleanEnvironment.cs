namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    // The CLI keeps the machine's environment (a company gateway, Bedrock/Vertex variables), except what would make it
    // call AIT again (a loopback ANTHROPIC_BASE_URL is AIT's own proxy, set by the CLI menu) or refuse to start as a
    // nested Claude Code session.
    public static void CleanEnvironment(IDictionary<string, string?> environment)
    {
        foreach (var name in new[] { "CLAUDECODE", "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_CODE_SSE_PORT" }) environment.Remove(name);

        if (environment.TryGetValue("ANTHROPIC_BASE_URL", out var baseUrl) && IsLoopback(baseUrl))
        {
            environment.Remove("ANTHROPIC_BASE_URL");
            environment.Remove("ANTHROPIC_AUTH_TOKEN");
            environment.Remove("ANTHROPIC_CUSTOM_HEADERS");
        }

        // Every call is a new process; none of them should start an update.
        environment["DISABLE_AUTOUPDATER"] = "1";
    }
}
