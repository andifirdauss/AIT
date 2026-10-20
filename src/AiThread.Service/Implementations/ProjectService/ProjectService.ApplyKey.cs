using AiThread.Common.Entities;
using AiThread.Service.Workspace;

namespace AiThread.Service.Implementations;

public sealed partial class ProjectService
{
    // Recomputes the project's key from its folder; returns the previous key. A folder that is gone keeps its key.
    private static string ApplyKey(Project project)
    {
        var previous = project.ProjectKey;
        if (!Directory.Exists(project.RootPath) && !string.IsNullOrEmpty(previous)) return previous;

        var info = ProjectKeyResolver.Resolve(project.RootPath, project.ManualKey);
        project.ProjectKey = info.Key;
        project.ProjectKeySource = info.Source;
        return previous;
    }
}
