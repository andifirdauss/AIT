using AIThread.Service.Workspace;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectService
{
    // What a session outside any project looks like in the log, so the user knows what to change.
    private static string NoProjectReason(string? subject, ProjectResolution? resolution)
    {
        if (subject is null) return "working directory tidak diketahui";
        if (resolution?.Nearby is { Count: > 0 } nearby)
            return $"folder ini berisi project {string.Join(", ", nearby.Take(5).Select(p => p.Name))}{(nearby.Count > 5 ? ", …" : string.Empty)}; buka sesi di salah satunya";
        if (ProjectLocator.IsBlockedRoot(subject)) return "folder ini terlalu luas untuk jadi project (drive, home, atau folder sistem)";
        return "tidak ada marker project seperti .git, .sln, pom.xml, atau package.json di folder ini maupun folder induknya";
    }
}
