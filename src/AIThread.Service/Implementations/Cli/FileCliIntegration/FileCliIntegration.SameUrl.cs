namespace AIThread.Service.Implementations.Cli;

public abstract partial class FileCliIntegration
{
    private static bool SameUrl(string? current, string expected) =>
        current is not null && string.Equals(current.Trim().TrimEnd('/'), expected.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}
