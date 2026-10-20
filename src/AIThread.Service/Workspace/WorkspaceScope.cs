namespace AIThread.Service.Workspace;

// Sandbox for every file operation of a thread. Writes (and the executors' work) are resolved against the project root
// and anything that escapes it, or points into build output / VCS folders, is rejected. The planner's read-only checks
// may go beyond the root (TryResolveForRead), e.g. to look at another project when the developer asks for it.
public sealed class WorkspaceScope
{
    // Folders that are never source in any stack: VCS, IDE, package caches and tool caches.
    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", ".nuget", "bin", "obj", "node_modules", "packages", "TestResults", "artifacts",
        ".gradle", ".venv", "__pycache__", ".mypy_cache", ".pytest_cache", ".tox", ".next", ".nuxt", ".svelte-kit",
        ".turbo", ".cache", ".dart_tool", ".terraform", "Pods"
    };

    private readonly HashSet<string> _extraIgnored;

    // extraIgnoredDirectories: build output that only counts as noise for the stacks of this project
    // (e.g. "target" for Maven), see ProjectLocator.IgnoredDirectoriesFor.
    public WorkspaceScope(string root, IEnumerable<string>? extraIgnoredDirectories = null)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        _extraIgnored = new HashSet<string>(extraIgnoredDirectories ?? [], StringComparer.OrdinalIgnoreCase);
    }

    public string Root { get; }

    public static bool IsIgnoredDirectoryName(string name) => IgnoredDirectoryNames.Contains(name);

    // Third-party client libraries are noise for planning and must never be edited.
    public static bool IsIgnoredRelativePath(string relativePath) => IsIgnoredRelativePath(relativePath, null);

    public bool IsIgnoredDirectory(string name) => IsIgnoredDirectoryName(name) || _extraIgnored.Contains(name);

    public bool IsIgnoredPath(string relativePath) => IsIgnoredRelativePath(relativePath, _extraIgnored);

    private static bool IsIgnoredRelativePath(string relativePath, HashSet<string>? extraIgnored)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (IsIgnoredDirectoryName(segments[i]) || extraIgnored?.Contains(segments[i]) == true) return true;
        }
        return normalized.Contains("wwwroot/lib/", StringComparison.OrdinalIgnoreCase);
    }

    // Ignore rule for a folder met while walking: by name, plus the path rules relative to the root when it lies inside
    // it. Outside the root only names and third-party client libraries count (a relative path would be full of "..").
    public bool IsIgnoredDirectoryPath(string fullPath) =>
        IsIgnoredDirectory(Path.GetFileName(fullPath))
        || (IsInside(fullPath)
            ? IsIgnoredPath(ToRelative(fullPath) + "/")
            : (fullPath.Replace('\\', '/') + "/").Contains("wwwroot/lib/", StringComparison.OrdinalIgnoreCase));

    // For anything that changes files: the path must stay inside the project root.
    public bool TryResolve(string? path, out string fullPath, out string? error)
    {
        if (!TryNormalize(path, out fullPath, out error)) return false;
        if (IsInside(fullPath)) return true;

        fullPath = string.Empty;
        error = $"Path '{path}' is outside the project root.";
        return false;
    }

    // For read-only checks by the planner: an absolute path (or one with "..") may point anywhere on this machine.
    public bool TryResolveForRead(string? path, out string fullPath, out string? error) => TryNormalize(path, out fullPath, out error);

    private bool TryNormalize(string? path, out string fullPath, out string? error)
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

        fullPath = Path.TrimEndingDirectorySeparator(candidate);
        return true;
    }

    public bool IsInside(string fullPath) =>
        string.Equals(fullPath, Root, StringComparison.OrdinalIgnoreCase)
        || fullPath.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public string ToRelative(string fullPath) => Path.GetRelativePath(Root, fullPath).Replace('\\', '/');

    // How a path is shown to the planner: relative to the root inside it, absolute (forward slashes) outside it.
    public string ToDisplay(string fullPath) => IsInside(fullPath) ? ToRelative(fullPath) : fullPath.Replace('\\', '/');

    // Depth-first walk that never descends into ignored folders (nor, outside the root, into junctions and symlinks,
    // which could loop).
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
                if (IsIgnoredDirectoryPath(child)) continue;
                if (!IsInside(child) && IsLink(child)) continue;
                pending.Push(child);
            }
        }
    }

    private static bool IsLink(string directory)
    {
        try
        {
            return new DirectoryInfo(directory).LinkTarget is not null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }
}
