namespace AIThread.Service.Implementations.Cli;

public sealed partial class CodexIntegration
{
    private static IReadOnlyList<string> Block(CliEntry entry) =>
    [
        $"[model_providers.{ProviderId}]",
        "name = \"AIT\"",
        $"base_url = {YamlProviderBlock.Quote(entry.BaseUrl)}",
        "wire_api = \"responses\"",
        $"experimental_bearer_token = {YamlProviderBlock.Quote(entry.ApiKey)}",
        "stream_idle_timeout_ms = 1800000",
        string.Empty,
        $"[profiles.{ProviderId}]",
        $"model_provider = \"{ProviderId}\""
    ];
}
