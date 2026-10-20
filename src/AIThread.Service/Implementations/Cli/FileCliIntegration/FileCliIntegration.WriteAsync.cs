using System.Text;

namespace AIThread.Service.Implementations.Cli;

public abstract partial class FileCliIntegration
{
    private static async Task WriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, new UTF8Encoding(false), cancellationToken);
    }
}
