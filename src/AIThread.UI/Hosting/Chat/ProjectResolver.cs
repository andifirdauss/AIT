using AIThread.Common.DTOs;
using AIThread.Common.Localization;

namespace AIThread.UI.Hosting.Chat;

// Which registered project a chat works in: --project by name or folder, otherwise the project whose root holds the
// current directory. Error is set when --project names nothing; both null = no match, the caller lets the developer
// pick (or exits with code 2 under -p).
public sealed record ProjectResolution(ProjectDto? Project, string? Error);

public static class ProjectResolver
{
    public static ProjectResolution Resolve(IReadOnlyList<ProjectDto> projects, string? wanted, string currentDirectory)
    {
        if (string.IsNullOrWhiteSpace(wanted)) return new ProjectResolution(ByFolder(projects, currentDirectory), null);

        wanted = wanted.Trim();
        var named = projects.Where(p => string.Equals(p.Name, wanted, StringComparison.OrdinalIgnoreCase)).ToList();
        if (named.Count == 1) return new ProjectResolution(named[0], null);
        if (named.Count > 1)
            return new ProjectResolution(null, Lang.T($"Ada {named.Count} project bernama '{wanted}'; pakai path foldernya.", $"There are {named.Count} projects named '{wanted}'; use its folder path."));

        string? folder = null;
        try
        {
            folder = Path.GetFullPath(wanted, currentDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }

        if (folder is not null && (Directory.Exists(folder) || File.Exists(folder)) && ByFolder(projects, folder) is { } inside)
            return new ProjectResolution(inside, null);

        return new ProjectResolution(null, Lang.T($"Project '{wanted}' tidak ditemukan (bukan nama project terdaftar maupun folder di dalamnya).", $"Project '{wanted}' was not found (neither the name of a registered project nor a folder inside one)."));
    }

    // The deepest registered root that is the folder or one of its parents.
    public static ProjectDto? ByFolder(IReadOnlyList<ProjectDto> projects, string folder)
    {
        var path = Normalize(folder);
        return projects
            .Where(p => !string.IsNullOrWhiteSpace(p.RootPath) && Contains(Normalize(p.RootPath), path))
            .OrderByDescending(p => Normalize(p.RootPath).Length)
            .ThenByDescending(p => p.Exists)
            .FirstOrDefault();
    }

    private static bool Contains(string root, string path) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || (root.EndsWith(Path.DirectorySeparatorChar) && path.StartsWith(root, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string path)
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
}
