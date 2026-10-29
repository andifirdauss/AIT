using AIThread.Service.Models;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Memory;

public sealed partial class CodeMapper
{
    // Project files a text refers to: paths that exist, and identifiers matched to file names (code map first, then the
    // files on disk; stored paths missing from this clone are skipped). Matches are ordered by ReferenceAreas before the list is cut, so a repository holding the same
    // names twice (a template and its sample) lists the app holding most of the names first, not the first on disk.
    public async Task<IReadOnlyList<string>> ResolveReferencesAsync(ProjectContext project, string? text, CancellationToken cancellationToken)
    {
        var (paths, identifiers) = CodeMapHeuristics.References(text);
        var result = Normalize(project, paths).ToList();
        if (identifiers.Count == 0 || string.IsNullOrEmpty(project.Key)) return result;

        var wanted = identifiers.Where(i => !result.Any(p => CodeMapHeuristics.FileStem(p).Equals(i, StringComparison.OrdinalIgnoreCase))).ToList();
        if (wanted.Count == 0) return result;

        var matches = new List<string>();
        await using (var db = await store.CreateCatalogAsync(cancellationToken))
        {
            foreach (var identifier in wanted.ToList())
            {
                var marker = "/" + identifier + ".";
                var key = project.Key;
                var found = await db.CodeMapEntries.AsNoTracking()
                    .Where(e => e.ProjectKey == key && (e.Path.Contains(marker) || e.Path.StartsWith(identifier + ".")))
                    .Select(e => e.Path)
                    .Take(MaxStoredMatches)
                    .ToListAsync(cancellationToken);
                // The code map is shared by every clone of the key, so it can hold files of another branch or machine.
                found = found.Where(p => File.Exists(Path.Combine(project.RootPath, p))).ToList();
                if (found.Count == 0) continue;
                matches.AddRange(found);
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
                if (!scope.IsIgnoredPath(relative)) matches.Add(relative);
                if (matches.Count >= MaxScanMatches) break;
            }
        }

        var ordered = ReferenceAreas.Order(project.RootPath, matches.Distinct(StringComparer.OrdinalIgnoreCase), identifiers);
        return result.Concat(ordered).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxReferences).ToList();
    }
}
