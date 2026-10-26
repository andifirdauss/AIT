namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // A comma-separated list of commits.
    private static IReadOnlyList<string> Split(string? csv) =>
        (csv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
