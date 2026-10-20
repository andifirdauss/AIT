namespace AiThread.Service.Implementations.Cli;

public sealed partial class OhMyPiProviderIntegration
{
    // The provider entry, relative to its own indentation. AiThread picks the real models, so the window and output
    // limits only steer omp's own compaction and request size.
    private async Task<IReadOnlyList<string>> ProviderLinesAsync(string proxyBaseUrl, CancellationToken cancellationToken)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        return
        [
            $"{ProviderId}:",
            $"  baseUrl: {YamlProviderBlock.Quote(proxyBaseUrl.Trim().TrimEnd('/'))}",
            "  api: anthropic-messages",
            $"  apiKey: {YamlProviderBlock.Quote(settings.ProxyApiKey ?? "aithread-local")}",
            "  models:",
            $"    - id: {ModelId}",
            $"      name: {ModelId}",
            "      contextWindow: 1000000",
            "      maxTokens: 32000",
            "      input: [text, image]"
        ];
    }
}
