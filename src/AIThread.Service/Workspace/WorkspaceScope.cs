using AIThread.Service.Security;

namespace AIThread.Service.Workspace;

// Sandbox for every file operation of a thread. Writes (and the executors' work) are resolved against the project root
// and anything that escapes it, or points into build output / VCS folders, is rejected, unless the developer allowed
// the folder for this thread (AllowWrites, after an ApprovalKind.WriteOutside approval). The planner's read-only
// checks may go beyond the root (TryResolveForRead), e.g. to look at another project when the developer asks for it.
// Credential files (SecretPaths, plus the project's own rules in .aithread/project.json) are refused both ways.
public sealed class WorkspaceScope
{
    // Folders that are never source in any stack: VCS, IDE, package caches and tool caches.
    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", ".nuget", "bin", "obj", "node_modules", "packages", "TestResults", "artifacts",
        ".gradle", ".venv", "__pycache__", ".mypy_cache", ".pytest_cache", ".tox", ".next", ".nuxt", ".svelte-kit",
        ".turbo", ".cache", ".dart_tool", ".terraform", "Pods"
    };

    // Folders that never take a write from AIT, whatever the developer allows: the system and installed programs.
    private static readonly Lazy<string[]> SystemFolders = new(() => new[]
        {
            Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData
        }
        .Select(Environment.GetFolderPath)
        .Where(f => !string.IsNullOrWhiteSpace(f))
        .Select(f => Path.TrimEndingDirectorySeparator(Path.GetFullPath(f)))
        .ToArray());

    private readonly HashSet<string> _extraIgnored;
    private readonly List<string> _writableRoots = [];
    private SecretPolicy? _secrets;

    // extraIgnoredDirectories: build output that only counts as noise for the stacks of this project
    // (e.g. "target" for Maven), see ProjectLocator.IgnoredDirectoriesFor.
    public WorkspaceScope(string root, IEnumerable<string>? extraIgnoredDirectories = null)
    {
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        _extraIgnored = new HashSet<string>(extraIgnoredDirectories ?? [], StringComparer.OrdinalIgnoreCase);
    }

    public string Root { get; }

    // Folders (or single files) outside the root this thread may change, allowed by the developer.
    public IReadOnlyList<string> WritableRoots
    {
        get
        {
            lock (_writableRoots) return _writableRoots.ToList();
        }
    }

    public void AllowWrites(IEnumerable<string> targets)
    {
        lock (_writableRoots)
        {
            foreach (var target in targets)
            {
                var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
                if (!_writableRoots.Contains(full, StringComparer.OrdinalIgnoreCase)) _writableRoots.Add(full);
            }
        }
    }

    // The project's secret rules (.aithread/project.json "secrets"), read once per scope.
    public SecretPolicy Secrets => _secrets ??= SecretPolicy.Load(Root);

    // A credential file: never read for a model, never written.
    public bool IsProtected(string fullPath) => SecretPaths.IsProtected(fullPath, Root, Secrets);

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
        if (!TryNormalize(path, out fullPath, out error) || Refuse(path, ref fullPath, out error)) return false;
        if (IsInside(fullPath)) return true;

        fullPath = string.Empty;
        error = $"Path '{path}' is outside the project root.";
        return false;
    }

    // For anything that writes: inside the project root, or inside a folder the developer allowed (AllowWrites).
    public bool TryResolveForWrite(string? path, out string fullPath, out string? error)
    {
        if (!TryNormalize(path, out fullPath, out error) || Refuse(path, ref fullPath, out error)) return false;
        if (IsInside(fullPath) || IsWritableOutside(fullPath)) return true;

        fullPath = string.Empty;
        error = $"Path '{path}' is outside the project root and outside the folders the developer allowed for this thread.";
        return false;
    }

    // A path outside the root a task may change once the developer allows it: the full path and the folder the
    // permission is asked for (its repository or project root, else its folder, else the file itself when the folder is
    // a home, desktop or drive root). False with the reason when no permission can cover it.
    public bool TryResolveOutside(string? path, out string fullPath, out string target, out string? error)
    {
        target = string.Empty;
        if (!TryNormalize(path, out fullPath, out error) || Refuse(path, ref fullPath, out error)) return false;
        if (IsInside(fullPath))
        {
            error = $"Path '{path}' is inside the project root.";
            return false;
        }
        if (IsSystemPath(fullPath) || ProjectLocator.IsBlockedRoot(fullPath))
        {
            error = $"Path '{path}' is a system or program folder; AIT never changes files there.";
            fullPath = string.Empty;
            return false;
        }

        var directory = Path.GetDirectoryName(fullPath);
        while (directory is not null && !Directory.Exists(directory)) directory = Path.GetDirectoryName(directory);
        if (directory is not null && ProjectLocator.Resolve(directory).Root is { } project && !ProjectLocator.IsBlockedRoot(project.Path))
            target = Path.TrimEndingDirectorySeparator(project.Path);
        else if (directory is not null && !ProjectLocator.IsBlockedRoot(directory))
            target = Path.TrimEndingDirectorySeparator(directory);
        else
            target = fullPath;
        return true;
    }

    public bool IsWritableOutside(string fullPath)
    {
        lock (_writableRoots)
            return _writableRoots.Any(root => string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
                                             || fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSystemPath(string fullPath) =>
        SystemFolders.Value.Any(folder => string.Equals(fullPath, folder, StringComparison.OrdinalIgnoreCase)
                                          || fullPath.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

    // For read-only checks by the planner: an absolute path (or one with "..") may point anywhere on this machine.
    public bool TryResolveForRead(string? path, out string fullPath, out string? error) =>
        TryNormalize(path, out fullPath, out error) && !Refuse(path, ref fullPath, out error);

    private bool Refuse(string? path, ref string fullPath, out string? error)
    {
        error = null;
        if (!IsProtected(fullPath)) return false;
        SecretPaths.CountBlocked();
        error = SecretPaths.Refusal(path ?? fullPath);
        fullPath = string.Empty;
        return true;
    }

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
    // Written files are recorded the same way (journal, tasks, report).
    public string ToDisplay(string fullPath) => IsInside(fullPath) ? ToRelative(fullPath) : fullPath.Replace('\\', '/');

    // The full path of a recorded file: relative to the root, or absolute for a file outside it.
    public string FromStored(string storedPath) =>
        Path.GetFullPath(Path.IsPathRooted(storedPath) ? storedPath : Path.Combine(Root, storedPath));

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
