namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    private static string? Clip(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}
