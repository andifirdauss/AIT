namespace AiThread.Service.Workspace;

// Sandbox for every file operation of a thread: paths are resolved against the project root
// and anything that escapes it (or points into build output / VCS folders) is rejected.
public sealed class WorkspaceScope
{
    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", ".nuget", "bin", "obj", "node_modules", "packages", "TestResults", "artifacts"
    };

    public WorkspaceScope(string root)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    public string Root { get; }

    public static bool IsIgnoredDirectoryName(string name) => IgnoredDirectoryNames.Contains(name);

    // Third-party client libraries are noise for planning and must never be edited.
    public static bool IsIgnoredRelativePath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (IsIgnoredDirectoryName(segments[i])) return true;
        }
        return normalized.Contains("wwwroot/lib/", StringComparison.OrdinalIgnoreCase);
    }

    public bool TryResolve(string? path, out string fullPath, out string? error)
    {
        fullPath = string.Empty;
        error = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Path is empty.";
            return false;
        }

        var cleaned = path.Trim().Trim('"', '\'', '`').Replace('\\', '/');
        if (cleaned.StartsWith("./", StringComparison.Ordinal)) cleaned = cleaned[2..];

        string candidate;
        try
        {
            candidate = Path.IsPathRooted(cleaned)
                ? Path.GetFullPath(cleaned)
                : Path.GetFullPath(Path.Combine(Root, cleaned));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"Invalid path '{path}': {ex.Message}";
            return false;
        }

        candidate = Path.TrimEndingDirectorySeparator(candidate);
        if (!IsInside(candidate))
        {
            error = $"Path '{path}' is outside the project root.";
            return false;
        }

        fullPath = candidate;
        return true;
    }

    public bool IsInside(string fullPath) =>
        string.Equals(fullPath, Root, StringComparison.OrdinalIgnoreCase)
        || fullPath.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public string ToRelative(string fullPath) => Path.GetRelativePath(Root, fullPath).Replace('\\', '/');

    // Depth-first walk that never descends into ignored folders.
    public IEnumerable<string> EnumerateFiles(string? startDirectory = null, int maxFiles = 20_000)
    {
        var pending = new Stack<string>();
        pending.Push(startDirectory ?? Root);
        var yielded = 0;

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] files, directories;
            try
            {
                files = Directory.GetFiles(directory);
                directories = Directory.GetDirectories(directory);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                yield return file;
                if (++yielded >= maxFiles) yield break;
            }

            foreach (var child in directories.OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(child);
                if (IsIgnoredDirectoryName(name)) continue;
                if (IsIgnoredRelativePath(ToRelative(child) + "/")) continue;
                pending.Push(child);
            }
        }
    }
}
