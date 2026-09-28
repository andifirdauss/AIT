using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AiThread.Service.Workspace;

public static partial class SolutionLocator
{
    private const int MaxParentLevels = 6;
    private const int MaxChildDepth = 2;

    public static bool IsSolutionFile(string path) =>
        path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);

    // Looks in the directory itself, then shallow children (a session opened at the repository root
    // above src/), then parents (a session opened in a sub-folder of the solution).
    public static string? Find(string startPath)
    {
        if (File.Exists(startPath)) return IsSolutionFile(startPath) ? Path.GetFullPath(startPath) : Find(Path.GetDirectoryName(startPath)!);
        if (!Directory.Exists(startPath)) return null;

        var directory = new DirectoryInfo(Path.GetFullPath(startPath));
        var inPlace = PickSolution(directory);
        if (inPlace is not null) return inPlace;

        // Several solutions below the working directory is ambiguous (e.g. a folder holding many repos): pick none.
        var candidates = new List<string>();
        CollectChildren(directory, 1, candidates);
        if (candidates.Count == 1) return candidates[0];
        if (candidates.Count > 1) return null;

        var parent = directory.Parent;
        for (var level = 0; parent is not null && level < MaxParentLevels; level++, parent = parent.Parent)
        {
            var found = PickSolution(parent);
            if (found is not null) return found;
        }

        return null;
    }

    // Relative project paths declared in the solution.
    public static IReadOnlyList<string> ReadProjects(string solutionPath)
    {
        try
        {
            if (solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                var document = XDocument.Load(solutionPath);
                return document.Descendants()
                    .Where(e => e.Name.LocalName == "Project")
                    .Select(e => e.Attribute("Path")?.Value)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p!.Replace('\\', '/'))
                    .ToList();
            }

            return File.ReadLines(solutionPath)
                .Select(line => SlnProjectLine().Match(line))
                .Where(m => m.Success && m.Groups[2].Value.EndsWith("proj", StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Groups[2].Value.Replace('\\', '/'))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return [];
        }
    }

    private static string? PickSolution(DirectoryInfo directory)
    {
        FileInfo[] files;
        try
        {
            files = directory.GetFiles("*.sln*").Where(f => IsSolutionFile(f.Name)).ToArray();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return null;
        }

        if (files.Length == 0) return null;
        if (files.Length == 1) return files[0].FullName;

        // Prefer the solution named after its folder, then the newer .slnx format.
        return files
            .OrderByDescending(f => string.Equals(Path.GetFileNameWithoutExtension(f.Name), directory.Name, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(f => f.Extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .First().FullName;
    }

    private static void CollectChildren(DirectoryInfo directory, int depth, List<string> found)
    {
        if (depth > MaxChildDepth) return;
        DirectoryInfo[] children;
        try
        {
            children = directory.GetDirectories();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return;
        }

        foreach (var child in children)
        {
            if (WorkspaceScope.IsIgnoredDirectoryName(child.Name) || child.Name.StartsWith('.')) continue;
            var solution = PickSolution(child);
            if (solution is not null) found.Add(solution);
            else CollectChildren(child, depth + 1, found);
        }
    }

    // Project("{FAE04EC0-...}") = "Name", "path\Name.csproj", "{GUID}"
    [GeneratedRegex("""^Project\("\{[^}]+\}"\)\s*=\s*"([^"]+)",\s*"([^"]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex SlnProjectLine();
}
