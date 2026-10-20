namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    private static string Truncate(string? value, int max) =>
        value is null ? string.Empty : value.Length <= max ? value : value[..max];
}
