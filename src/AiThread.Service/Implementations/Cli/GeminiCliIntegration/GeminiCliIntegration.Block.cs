namespace AiThread.Service.Implementations.Cli;

public sealed partial class GeminiCliIntegration
{
    private static IReadOnlyList<string> Block(CliEntry entry) =>
    [
        $"{BaseUrlKey}=\"{entry.BaseUrl}\"",
        $"{ApiKeyKey}=\"{entry.ApiKey}\""
    ];
}
