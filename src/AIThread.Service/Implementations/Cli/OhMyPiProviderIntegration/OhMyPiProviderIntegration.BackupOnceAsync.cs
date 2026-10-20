namespace AIThread.Service.Implementations.Cli;

public sealed partial class OhMyPiProviderIntegration
{
    private async Task BackupOnceAsync(CancellationToken cancellationToken)
    {
        var backup = _modelsPath + BackupSuffix;
        if (File.Exists(_modelsPath) && !File.Exists(backup))
            await File.WriteAllBytesAsync(backup, await File.ReadAllBytesAsync(_modelsPath, cancellationToken), cancellationToken);
    }
}
