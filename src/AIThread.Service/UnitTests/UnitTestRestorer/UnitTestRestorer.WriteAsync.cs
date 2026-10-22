using System.Text;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestRestorer
{
    // The snapshot is written as it was kept, line endings included. Only files inside the root are ever written.
    private static async Task WriteAsync(string root, string relative, string content, CancellationToken cancellationToken)
    {
        var full = Path.GetFullPath(Full(root, relative));
        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, content, new UTF8Encoding(false), cancellationToken);
    }
}
