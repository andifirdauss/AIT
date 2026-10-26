using AIThread.Common.Localization;
using AIThread.Service.Workspace;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectService
{
    // What a session outside any project looks like in the log, so the user knows what to change.
    private static string NoProjectReason(string? subject, ProjectResolution? resolution)
    {
        if (subject is null) return Lang.T("working directory tidak diketahui", "unknown working directory");
        if (resolution?.Nearby is { Count: > 0 } nearby)
            return Lang.T($"folder ini berisi project {string.Join(", ", nearby.Take(5).Select(p => p.Name))}{(nearby.Count > 5 ? ", …" : string.Empty)}; buka sesi di salah satunya", $"this folder holds the projects {string.Join(", ", nearby.Take(5).Select(p => p.Name))}{(nearby.Count > 5 ? ", …" : string.Empty)}; open the session in one of them");
        if (ProjectLocator.IsBlockedRoot(subject)) return Lang.T("folder ini terlalu luas untuk jadi project (drive, home, atau folder sistem)", "this folder is too broad to be a project (a drive, home or system folder)");
        return Lang.T("tidak ada marker project seperti .git, .sln, pom.xml, atau package.json di folder ini maupun folder induknya", "there is no project marker such as .git, .sln, pom.xml or package.json in this folder or its parent folders");
    }
}
