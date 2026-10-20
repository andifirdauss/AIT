namespace AIThread.Service.Orchestration;

public sealed partial class FileOperations
{
    private static ExecutorOutcome Failed(string? error) => new(false, null, null, error ?? "Path tidak valid.", 0, 1);
}
