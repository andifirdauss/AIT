using System.IO.Compression;
using AIThread.Common.Localization;

namespace AIThread.Service.Skills;

// Unpacks a repository archive into a folder: the single top-level folder GitHub/GitLab put around the files is
// dropped, only subPath is kept when given, and entries escaping the destination (zip slip) are refused.
public static class SkillArchive
{
    public const long MaxTotalBytes = 300L * 1024 * 1024;
    public const long MaxFileBytes = 20L * 1024 * 1024;
    public const int MaxFiles = 5_000;

    private static readonly string[] SkippedSegments = [".git", "node_modules", "__pycache__", ".venv"];

    // Returns how many files were written.
    public static int Extract(Stream zip, string destination, string? subPath)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        var files = archive.Entries.Where(e => !e.FullName.EndsWith('/') && !e.FullName.EndsWith('\\')).ToList();
        var prefix = CommonTopFolder(files.Select(e => Normalize(e.FullName)));
        var keep = string.IsNullOrWhiteSpace(subPath) ? null : Normalize(subPath).Trim('/') + "/";

        var root = Path.GetFullPath(destination);
        Directory.CreateDirectory(root);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;

        var written = 0;
        long total = 0;
        foreach (var entry in files)
        {
            var relative = Normalize(entry.FullName);
            if (prefix is not null) relative = relative[prefix.Length..];
            if (keep is not null)
            {
                if (!relative.StartsWith(keep, StringComparison.OrdinalIgnoreCase)) continue;
                relative = relative[keep.Length..];
            }
            if (relative.Length == 0 || relative.Split('/').Any(s => SkippedSegments.Contains(s, StringComparer.OrdinalIgnoreCase))) continue;
            if (entry.Length > MaxFileBytes) continue;

            var target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(Lang.T($"Arsip berisi path berbahaya: {entry.FullName}", $"The archive contains a dangerous path: {entry.FullName}"));

            total += entry.Length;
            if (total > MaxTotalBytes) throw new InvalidDataException(Lang.T("Isi repository terlalu besar (lebih dari 300 MB).", "The repository content is too large (more than 300 MB)."));
            if (++written > MaxFiles) throw new InvalidDataException(Lang.T($"Repository berisi lebih dari {MaxFiles} file.", $"The repository holds more than {MaxFiles} files."));

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }

        if (keep is not null && written == 0)
            throw new InvalidDataException(Lang.T($"Folder '{subPath}' tidak ditemukan di repository.", $"Folder '{subPath}' not found in the repository."));
        return written;
    }

    // "repo-abc123/" when every entry lives under that one folder.
    private static string? CommonTopFolder(IEnumerable<string> paths)
    {
        string? top = null;
        foreach (var path in paths)
        {
            var slash = path.IndexOf('/');
            if (slash <= 0) return null;
            var first = path[..(slash + 1)];
            if (top is null) top = first;
            else if (!string.Equals(top, first, StringComparison.Ordinal)) return null;
        }
        return top;
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');
}
