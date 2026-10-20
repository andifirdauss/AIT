namespace AiThread.Service.Implementations.Cli;

public sealed partial class GeminiCliIntegration
{
    protected override string? CurrentBaseUrl(string content) =>
        ManagedTextBlock.Managed(content)?.Select(l => Assignment().Match(l))
            .FirstOrDefault(m => m.Success && m.Groups["key"].Value == BaseUrlKey)?.Groups["value"].Value;
}
