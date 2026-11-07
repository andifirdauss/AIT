namespace AIThread.Service.CliBackend;

public sealed partial class CodexCliAdapter
{
    // The CLI keeps the machine's environment (a company gateway, an API key of its own), except an OPENAI_BASE_URL on
    // loopback, which is AIT's own proxy and would make the call come back here.
    public static void CleanEnvironment(IDictionary<string, string?> environment)
    {
        if (environment.TryGetValue("OPENAI_BASE_URL", out var baseUrl) && CliProcess.IsLoopback(baseUrl))
        {
            environment.Remove("OPENAI_BASE_URL");
            environment.Remove("OPENAI_API_KEY");
        }
    }
}
