namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    private static string? Existing(string path) => File.Exists(path) ? path : null;
}
