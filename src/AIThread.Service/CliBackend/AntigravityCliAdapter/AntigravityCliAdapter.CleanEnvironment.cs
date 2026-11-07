namespace AIThread.Service.CliBackend;

public sealed partial class AntigravityCliAdapter
{
    // Same guard as Gemini CLI (both are Google's and read GEMINI_API_KEY with the "gemini" model provider): a Gemini
    // base URL on loopback, AIT's own proxy, is replaced by Google's endpoint.
    public static void CleanEnvironment(IDictionary<string, string?> environment, string? workFolder = null) =>
        GeminiCliAdapter.CleanEnvironment(environment, workFolder);
}
