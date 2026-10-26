using AIThread.Service.Orchestration;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // SHA-256 of the file on disk as FileJournal hashes it; null when it does not exist. A file that cannot be read
    // right now gets a hash matching nothing, so it is never taken for the thread's own content.
    private static string? CurrentHash(string root, string relativePath)
    {
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            return File.Exists(fullPath) ? FileJournal.Hash(File.ReadAllBytes(fullPath)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Unreadable;
        }
    }
}
