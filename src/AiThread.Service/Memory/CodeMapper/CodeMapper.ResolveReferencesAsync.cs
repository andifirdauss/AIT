using AiThread.Service.Models;
using AiThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Memory;

public sealed partial class CodeMapper
{
    // Project files a text refers to: paths that exist, and identifiers matched to file names (code map first, then the
    // files on disk).
    public async Task<IReadOnlyList<string>> ResolveReferencesAsync(ProjectContext project, string? text, CancellationToken cancellationToken)
    {
        var (paths, identifiers) = CodeMapHeuristics.References(text);
        var result = Normalize(project, paths).ToList();
        if (identifiers.Count == 0 || string.IsNullOrEmpty(project.Key)) return result;

        var wanted = identifiers.Where(i => !result.Any(p => CodeMapHeuristics.FileStem(p).Equals(i, StringComparison.OrdinalIgnoreCase))).ToList();
        if (wanted.Count == 0) return result;

        await using (var db = await store.CreateCatalogAsync(cancellationToken))
        {
            foreach (var identifier in wanted.ToList())
            {
                var marker = "/" + identifier + ".";
                var key = project.Key;
                var found = await db.CodeMapEntries.AsNoTracking()
                    .Where(e => e.ProjectKey == key && (e.Path.Contains(marker) || e.Path.StartsWith(identifier + ".")))
                    .Select(e => e.Path)
                    .Take(3)
                    .ToListAsync(cancellationToken);
                if (found.Count == 0) continue;
                result.AddRange(found);
                wanted.Remove(identifier);
            }
        }

        if (wanted.Count > 0 && Directory.Exists(project.RootPath))
        {
            var scope = new WorkspaceScope(project.RootPath, ProjectLocator.IgnoredDirectoriesFor(project.Stacks));
            var names = new HashSet<string>(wanted, StringComparer.OrdinalIgnoreCase);
            foreach (var file in scope.EnumerateFiles(maxFiles: 20_000))
            {
                if (!names.Contains(CodeMapHeuristics.FileStem(Path.GetFileName(file)))) continue;
                var relative = scope.ToRelative(file);
                if (!scope.IsIgnoredPath(relative)) result.Add(relative);
                if (result.Count >= 30) break;
            }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
