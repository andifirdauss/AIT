namespace AIThread.Common.Projects;

// Which registered root holds a folder: shared by aithread chat, its agent commands and the skill gate.
public static class ProjectFolders
{
    // The items whose root is the folder or one of its parents, deepest root first.
    public static IOrderedEnumerable<T> Holding<T>(IEnumerable<T> items, Func<T, string?> rootOf, string folder)
    {
        var path = Normalize(folder);
        return items
            .Where(item => rootOf(item) is { } root && !string.IsNullOrWhiteSpace(root) && Contains(Normalize(root), path))
            .OrderByDescending(item => Normalize(rootOf(item)!).Length);
    }

    public static bool IsInside(string folder, string root) => Contains(Normalize(root), Normalize(folder));

    public static string Normalize(string path)
    {
        try
        {
            path = Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }

        // C:\ keeps its separator; any other root loses the trailing one.
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.EndsWith(':') ? trimmed + Path.DirectorySeparatorChar : trimmed;
    }

    private static bool Contains(string root, string path) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || (root.EndsWith(Path.DirectorySeparatorChar) && path.StartsWith(root, StringComparison.OrdinalIgnoreCase));
}
