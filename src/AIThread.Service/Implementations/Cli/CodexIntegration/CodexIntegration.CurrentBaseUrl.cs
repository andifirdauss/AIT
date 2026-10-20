namespace AIThread.Service.Implementations.Cli;

public sealed partial class CodexIntegration
{
    protected override string? CurrentBaseUrl(string content) =>
        ManagedTextBlock.Managed(content)?.Select(l => BaseUrlLine().Match(l)).FirstOrDefault(m => m.Success)?.Groups["url"].Value;
}
