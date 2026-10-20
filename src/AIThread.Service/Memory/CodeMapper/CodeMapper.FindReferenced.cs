using AIThread.Common.Entities;
using AIThread.Service.Models;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Memory;

public sealed partial class CodeMapper
{
    // One walk of the project for every path and identifier the texts name (the first LookupReferences of each text),
    // at most FilesPerReference files each (an identifier like "Index" names many files).
    private static List<string> FindReferenced(ProjectContext project, IEnumerable<string?> texts)
    {
        var pathsByName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var text in texts)
        {
            var (paths, names) = CodeMapHeuristics.References(text, LookupReferences);
            foreach (var path in paths.Select(p => p.Replace('\\', '/').TrimStart('.', '/')).Where(p => p.Length > 0))
            {
                var name = Path.GetFileName(path);
                if (!pathsByName.TryGetValue(name, out var list)) pathsByName[name] = list = [];
                if (!list.Contains(path, StringComparer.OrdinalIgnoreCase)) list.Add(path);
            }
            identifiers.UnionWith(names);
        }
        if (pathsByName.Count == 0 && identifiers.Count == 0) return [];

        var scope = new WorkspaceScope(project.RootPath, ProjectLocator.IgnoredDirectoriesFor(project.Stacks));
        var perReference = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var file in scope.EnumerateFiles(maxFiles: 20_000))
        {
            var relative = scope.ToRelative(file);
            if (relative.Length > CodeMapEntry.MaxPathLength || scope.IsIgnoredPath(relative) || !CodeMapHeuristics.IsMappable(relative)) continue;

            var name = Path.GetFileName(relative);
            var reference = pathsByName.TryGetValue(name, out var candidates)
                ? candidates.FirstOrDefault(p => relative.Equals(p, StringComparison.OrdinalIgnoreCase) || relative.EndsWith("/" + p, StringComparison.OrdinalIgnoreCase))
                : null;
            var stem = CodeMapHeuristics.FileStem(name);
            if (reference is null && identifiers.Contains(stem)) reference = "#" + stem;
            if (reference is null) continue;

            var count = perReference.GetValueOrDefault(reference);
            if (count >= FilesPerReference) continue;
            perReference[reference] = count + 1;
            result.Add(relative);
        }
        return result;
    }
}
