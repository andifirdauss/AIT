namespace AiThread.Service.Orchestration;

public sealed partial class SessionHandoff
{
    // Only transcripts inside Claude Code's own folder: the hook endpoint must not become a way to read any file.
    internal static string? ValidTranscriptPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
        if (!full.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return null;

        var configDirectories = new[]
        {
            Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
        };
        return configDirectories
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => Path.GetFullPath(Path.Combine(d!, "projects")) + Path.DirectorySeparatorChar)
            .Any(root => full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            ? full
            : null;
    }
}
