namespace AiThread.Service.Implementations.Cli;

public abstract partial class FileCliIntegration
{
    private async Task<CliEntry> EntryAsync(string proxyBaseUrl, string? projectRoot, CancellationToken cancellationToken)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        return new CliEntry(ConfigBaseUrl(proxyBaseUrl), settings.ProxyApiKey ?? "aithread-local", projectRoot);
    }
}
