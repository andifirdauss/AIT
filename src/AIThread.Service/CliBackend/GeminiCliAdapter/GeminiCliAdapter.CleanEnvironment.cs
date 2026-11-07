namespace AIThread.Service.CliBackend;

public sealed partial class GeminiCliAdapter
{
    // The CLI keeps the machine's environment (a company gateway, Vertex/Code Assist variables), except a
    // GOOGLE_GEMINI_BASE_URL on loopback: AIT's own proxy, set by the CLI menu in a .gemini/.env. Gemini CLI loads the
    // first .env found from its working folder upward (then the home one) without overriding a variable already set, so
    // the official endpoint is set in the environment whenever that .env, or the environment itself, points at AIT.
    // Only that one key of the .env is read. A GEMINI_API_KEY set in the environment next to a loopback base URL is
    // AIT's local key and is dropped; one in the .env stays, and Google rejects it as a login error (no loop).
    public static void CleanEnvironment(IDictionary<string, string?> environment, string? workFolder = null, string? home = null)
    {
        if (environment.TryGetValue(BaseUrlKey, out var baseUrl) && !string.IsNullOrWhiteSpace(baseUrl))
        {
            if (!CliProcess.IsLoopback(baseUrl)) return;
            environment[BaseUrlKey] = OfficialBaseUrl;
            environment.Remove("GEMINI_API_KEY");
            return;
        }

        var envFile = FindEnvFile(workFolder ?? CliProcess.WorkRoot, home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (envFile is not null && CliProcess.IsLoopback(EnvValue(envFile, BaseUrlKey))) environment[BaseUrlKey] = OfficialBaseUrl;
    }
}
