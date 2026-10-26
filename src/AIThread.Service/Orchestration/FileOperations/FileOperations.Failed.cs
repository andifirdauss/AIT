using AIThread.Common.Localization;

namespace AIThread.Service.Orchestration;

public sealed partial class FileOperations
{
    private static ExecutorOutcome Failed(string? error) => new(false, null, null, error ?? Lang.T("Path tidak valid.", "Invalid path."), 0, 1);
}
