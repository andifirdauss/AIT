namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // " (not in this branch: committed on feature/x by Andi)", or nothing.
    public static string Note(string? gitNote) => gitNote is null ? string.Empty : $" ({gitNote})";
}
