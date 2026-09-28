using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AiThread.Service.Workspace;

public sealed record SolutionScanResult(IReadOnlyList<string> Solutions, int FoldersScanned, bool Truncated);

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

    // Breadth-first search for solution files below a folder, nearest first. Stops at the depth, result or folder
    // limit, or when cancelled (the caller applies a time limit), and reports whether it stopped early.
    public static SolutionScanResult Scan(string root, int maxDepth, int maxResults, int maxFolders, CancellationToken cancellationToken)
    {
        var solutions = new List<string>();
        var pending = new Queue<(DirectoryInfo Directory, int Depth)>();
        pending.Enqueue((new DirectoryInfo(root), 0));
        var scanned = 0;

        while (pending.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested || solutions.Count >= maxResults || scanned >= maxFolders)
                return new SolutionScanResult(solutions, scanned, Truncated: true);

            var (directory, depth) = pending.Dequeue();
            scanned++;
            solutions.AddRange(SolutionsIn(directory).Take(maxResults - solutions.Count));

            if (depth >= maxDepth) continue;
            foreach (var child in BrowsableDirectories(directory)) pending.Enqueue((child, depth + 1));
        }

        return new SolutionScanResult(solutions, scanned, Truncated: false);
    }

    public static IReadOnlyList<string> SolutionsIn(DirectoryInfo directory)
    {
        try
        {
            return directory.GetFiles("*.sln*")
                .Where(f => IsSolutionFile(f.Name))
                .Select(f => f.FullName)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return [];
        }
    }

    // Sub-folders worth showing when looking for solutions: no hidden/system folders, no build output or VCS
    // folders, and no junctions/symlinks (following them could loop). Cloud placeholders (OneDrive) stay visible.
    public static IReadOnlyList<DirectoryInfo> BrowsableDirectories(DirectoryInfo directory)
    {
        DirectoryInfo[] children;
        try
        {
            children = directory.GetDirectories();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return [];
        }

        var result = new List<DirectoryInfo>(children.Length);
        foreach (var child in children)
        {
            try
            {
                if (child.Name.StartsWith('.') || child.Name.StartsWith('$') || WorkspaceScope.IsIgnoredDirectoryName(child.Name)) continue;
                if ((child.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                if (child.LinkTarget is not null) continue;
                result.Add(child);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // Unreadable entry — skip it.
            }
        }

        return result.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
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
