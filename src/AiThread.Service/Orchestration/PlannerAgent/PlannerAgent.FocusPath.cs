using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // Where inside the project the session was opened, when that is below the root (a repository root can hold several apps).
    private static string? FocusPath(WorkspaceScope scope, string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !scope.TryResolve(workingDirectory, out var full, out _)) return null;
        var relative = scope.ToRelative(full);
        return relative is "." or "" ? null : relative;
    }
}
