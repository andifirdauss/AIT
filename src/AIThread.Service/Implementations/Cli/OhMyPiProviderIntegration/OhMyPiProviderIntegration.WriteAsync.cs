using System.Text;

namespace AIThread.Service.Implementations.Cli;

public sealed partial class OhMyPiProviderIntegration
{
    private async Task WriteAsync(string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_modelsPath)!);
        await File.WriteAllTextAsync(_modelsPath, content, new UTF8Encoding(false), cancellationToken);
    }
}
