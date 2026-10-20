namespace AIThread.Service.Implementations.Cli;

public abstract partial class FileCliIntegration
{
    private static async Task BackupOnceAsync(string path, CancellationToken cancellationToken)
    {
        var backup = path + BackupSuffix;
        if (File.Exists(path) && !File.Exists(backup))
            await File.WriteAllBytesAsync(backup, await File.ReadAllBytesAsync(path, cancellationToken), cancellationToken);
    }
}
