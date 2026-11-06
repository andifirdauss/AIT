namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    // The end of stderr, where the CLI prints why it stopped.
    private static string? LastLines(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length == 0 ? null : trimmed.Length <= 600 ? trimmed : "…" + trimmed[^600..];
    }
}
