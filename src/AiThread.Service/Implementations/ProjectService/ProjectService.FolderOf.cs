namespace AiThread.Service.Implementations;

public sealed partial class ProjectService
{
    // A directory, or the folder of a file (the header used to carry the .sln path).
    private static string? FolderOf(string? path)
    {
        if (ExistingDirectory(path) is { } directory) return directory;
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var full = Path.GetFullPath(path.Trim().Trim('"'));
            return File.Exists(full) ? Path.GetDirectoryName(full) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
