namespace AiThread.Service.Orchestration;

public sealed partial class FileOperations
{
    // Removes the folder a file was taken out of, and its parents, while they are empty — never the root itself.
    public static void PruneEmptyDirectories(string directory, string root)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        while (current.Length > fullRoot.Length
               && current.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               && Directory.Exists(current)
               && !Directory.EnumerateFileSystemEntries(current).Any())
        {
            try
            {
                Directory.Delete(current);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
            current = Path.GetDirectoryName(current)!;
        }
    }
}
