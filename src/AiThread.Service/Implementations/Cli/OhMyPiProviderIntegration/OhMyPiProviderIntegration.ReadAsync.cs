namespace AiThread.Service.Implementations.Cli;

public sealed partial class OhMyPiProviderIntegration
{
    private async Task<string> ReadAsync(CancellationToken cancellationToken) =>
        File.Exists(_modelsPath) ? await File.ReadAllTextAsync(_modelsPath, cancellationToken) : string.Empty;
}
