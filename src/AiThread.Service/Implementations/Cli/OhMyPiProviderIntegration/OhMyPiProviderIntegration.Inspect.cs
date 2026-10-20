namespace AiThread.Service.Implementations.Cli;

public sealed partial class OhMyPiProviderIntegration
{
    private static (bool Enabled, string? CurrentBaseUrl) Inspect(string content, string proxyBaseUrl)
    {
        if (YamlProviderBlock.Managed(content) is not { } block) return (false, null);
        var current = block.Select(l => BaseUrlLine().Match(l)).FirstOrDefault(m => m.Success)?.Groups["url"].Value;
        // A block written under the old "aithread" provider id counts as not enabled, so turning it on again renames it.
        var enabled = current is not null && string.Equals(current.TrimEnd('/'), proxyBaseUrl.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
                      && block.Any(l => l.Trim() == ProviderId + ":");
        return (enabled, current);
    }
}
