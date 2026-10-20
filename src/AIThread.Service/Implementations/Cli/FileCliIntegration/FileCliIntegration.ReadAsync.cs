namespace AIThread.Service.Implementations.Cli;

public abstract partial class FileCliIntegration
{
    private static async Task<string> ReadAsync(string path, CancellationToken cancellationToken) =>
        File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : string.Empty;
}
