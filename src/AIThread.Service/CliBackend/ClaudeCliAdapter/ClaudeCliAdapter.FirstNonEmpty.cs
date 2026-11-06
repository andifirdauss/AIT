namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
