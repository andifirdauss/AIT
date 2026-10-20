using AiThread.Common.Entities;
using AiThread.Service.Models;
using AiThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Memory;

public sealed partial class CodeMapper
{
    // Relative paths inside the project, forward slashes, existing or not (a task may have deleted the file).
    private static List<string> Normalize(ProjectContext project, IEnumerable<string> paths)
    {
        var scope = new WorkspaceScope(project.RootPath);
        var result = new List<string>();
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !scope.TryResolve(path, out var full, out _)) continue;
            var relative = scope.ToRelative(full);
            if (relative is "." or "" || relative.Length > CodeMapEntry.MaxPathLength || scope.IsIgnoredPath(relative)) continue;
            if (!result.Contains(relative, StringComparer.OrdinalIgnoreCase)) result.Add(relative);
        }
        return result;
    }
}
