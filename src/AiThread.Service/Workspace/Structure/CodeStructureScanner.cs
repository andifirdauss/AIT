using System.Diagnostics;

namespace AiThread.Service.Workspace.Structure;

// Outline: null when the file is not parsed (no supported language, third-party, too large, unreadable).
public sealed record StructureFile(string Path, long Length, DateTime Modified, string? Language, bool ThirdParty, FileOutline? Outline);

// One walk of a project: every file outside the ignored folders, keyed by its relative path (forward slashes).
public sealed class StructureSnapshot
{
    private readonly Lazy<StructureClassIndex> _classes;

    public StructureSnapshot(IReadOnlyDictionary<string, StructureFile> files, int folders, bool truncated, long scanMs, int reparsed, DateTime scannedAt)
    {
        Files = files;
        Folders = folders;
        Truncated = truncated;
        ScanMs = scanMs;
        Reparsed = reparsed;
        ScannedAt = scannedAt;
        _classes = new Lazy<StructureClassIndex>(() => StructureClassIndex.Build(this));
    }

    public IReadOnlyDictionary<string, StructureFile> Files { get; }
    public int Folders { get; }
    public bool Truncated { get; }
    public long ScanMs { get; }
    public int Reparsed { get; }
    public DateTime ScannedAt { get; }

    // Built on the first class view, then reused until the next scan.
    public StructureClassIndex Classes => _classes.Value;
}

// Walks the project and outlines its source files in parallel. A file whose size and write time did not change since
// the previous snapshot keeps its outline, so a rescan only parses what changed.
public static class CodeStructureScanner
{
    public const int MaxFiles = 100_000;
    public const long MaxParseBytes = 2 * 1024 * 1024;

    public static StructureSnapshot Scan(string root, IEnumerable<string> ignoredDirectories, StructureSnapshot? previous, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var scope = new WorkspaceScope(root, ignoredDirectories);
        var paths = new List<string>();
        var truncated = false;
        foreach (var full in scope.EnumerateFiles(maxFiles: MaxFiles + 1))
        {
            if (paths.Count == MaxFiles)
            {
                truncated = true;
                break;
            }
            if (paths.Count % 1_000 == 0) cancellationToken.ThrowIfCancellationRequested();
            paths.Add(scope.ToRelative(full));
        }

        var filter = ThirdPartyFilter.Load(scope.Root, paths);
        var results = new StructureFile[paths.Count];
        var reparsed = 0;
        Parallel.For(0, paths.Count, new ParallelOptions { CancellationToken = cancellationToken }, i =>
        {
            var relative = paths[i];
            var info = new FileInfo(Path.Combine(scope.Root, relative));
            long length;
            DateTime modified;
            try
            {
                length = info.Length;
                modified = info.LastWriteTimeUtc;
            }
            catch (IOException)
            {
                length = 0;
                modified = DateTime.MinValue;
            }

            var language = OutlineLanguages.Of(relative);
            var thirdParty = filter.IsThirdParty(relative);
            FileOutline? outline = null;
            if (language is not null && !thirdParty && length <= MaxParseBytes)
            {
                if (previous?.Files.GetValueOrDefault(relative) is { Outline: not null } known && known.Length == length && known.Modified == modified)
                {
                    outline = known.Outline;
                }
                else
                {
                    outline = TryOutline(info.FullName, language);
                    Interlocked.Increment(ref reparsed);
                }
            }
            results[i] = new StructureFile(relative, length, modified, language, thirdParty, outline);
        });

        var files = new Dictionary<string, StructureFile>(results.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var file in results) files[file.Path] = file;
        var folders = paths.Select(p => Path.GetDirectoryName(p) ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return new StructureSnapshot(files, folders, truncated, clock.ElapsedMilliseconds, reparsed, DateTime.UtcNow);
    }

    private static FileOutline? TryOutline(string path, string language)
    {
        try
        {
            return OutlineLanguages.Outline(language, File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }
}
