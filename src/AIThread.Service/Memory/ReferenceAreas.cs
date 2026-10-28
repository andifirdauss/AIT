namespace AIThread.Service.Memory;

// Where the files a request names live when one repository holds several copies of the same names (a template and its
// sample, an old and a new app), grouped by the solution folder above each file. The group that holds most of the
// request's names comes first, then the shallowest: the request's own app leads the brief instead of the one that
// happens to come first on disk.
public static class ReferenceAreas
{
    private static readonly string[] SolutionExtensions = [".sln", ".slnx"];

    // Folder: relative to the root ("." for the root); Solution: its solution file name; Names: the request's names
    // that are file names in this area.
    public sealed record Area(string Folder, string? Solution, IReadOnlyList<string> Files, IReadOnlyList<string> Names);

    public static IReadOnlyList<Area> Group(string root, IEnumerable<string> files, IReadOnlyCollection<string> names)
    {
        var solutions = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        return files
            .GroupBy(f => FolderOf(root, f, solutions), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var stems = g.Select(f => CodeMapHeuristics.FileStem(Path.GetFileName(f))).ToHashSet(StringComparer.OrdinalIgnoreCase);
                return new Area(g.Key, solutions.GetValueOrDefault(g.Key), g.ToList(), names.Where(stems.Contains).ToList());
            })
            .OrderByDescending(a => a.Names.Count)
            .ThenBy(a => a.Folder == "." ? 0 : a.Folder.Count(c => c == '/') + 1)
            .ThenBy(a => a.Folder, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // The files in the order of their areas.
    public static IReadOnlyList<string> Order(string root, IEnumerable<string> files, IReadOnlyCollection<string> names) =>
        Group(root, files, names).SelectMany(a => a.Files).ToList();

    // A brief section when the request's names are in more than one area, so the planner chooses the app on purpose.
    public static string? Brief(string root, IEnumerable<string> files, IReadOnlyCollection<string> names, bool canAsk)
    {
        var areas = Group(root, files, names);
        if (areas.Count < 2) return null;

        var lines = areas.Select(a =>
        {
            var where = a.Folder == "." ? "the root" : a.Folder;
            var solution = a.Solution is null ? string.Empty : $" ({a.Solution})";
            var missing = names.Except(a.Names, StringComparer.OrdinalIgnoreCase).Where(n => areas.Any(o => o.Names.Contains(n, StringComparer.OrdinalIgnoreCase))).ToList();
            return $"- {where}{solution}: {a.Files.Count} file(s), has {(a.Names.Count == 0 ? "none of them" : string.Join(", ", a.Names))}"
                   + (missing.Count == 0 ? string.Empty : $"; lacks {string.Join(", ", missing)}");
        });
        var unclear = canAsk ? "When it stays unclear, ask the developer." : "When it stays unclear, take the first one and say so in the summary.";
        return "# The request's names are in several solutions\n"
               + "This repository holds more than one copy of the names the request uses (for example a template and its sample). Files the request names, by solution, the one holding most of the names first (the outline below follows this order):\n"
               + string.Join('\n', lines) + "\n"
               + $"Work in the solution the request means: the one that already has the names it treats as existing, else the root's own. Say which in the plan summary. {unclear}\n";
    }

    // The folder of the nearest solution above a file, inside the root; "." when only the root has one or none has.
    private static string FolderOf(string root, string relativeFile, Dictionary<string, string?> solutions)
    {
        var folder = Path.GetDirectoryName(relativeFile.Replace('\\', '/'))?.Replace('\\', '/') ?? string.Empty;
        while (folder.Length > 0)
        {
            if (!solutions.TryGetValue(folder, out var solution))
                solutions[folder] = solution = SolutionIn(Path.Combine(root, folder));
            if (solution is not null) return folder;
            folder = Path.GetDirectoryName(folder)?.Replace('\\', '/') ?? string.Empty;
        }
        if (!solutions.ContainsKey(".")) solutions["."] = SolutionIn(root);
        return ".";
    }

    private static string? SolutionIn(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory)
                    .Where(f => SolutionExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                    .Select(Path.GetFileName)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault()
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
