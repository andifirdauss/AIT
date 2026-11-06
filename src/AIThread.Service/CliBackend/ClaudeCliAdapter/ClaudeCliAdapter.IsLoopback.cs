namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    private static bool IsLoopback(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
        && (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));
}
