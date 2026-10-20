using System.Text.Json.Nodes;

namespace AIThread.Service.Implementations;

public sealed partial class ClaudeSettingsService
{
    private static async Task WriteAsync(string path, JsonObject root, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, root.ToJsonString(WriteOptions) + Environment.NewLine, cancellationToken);
    }
}
