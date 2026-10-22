namespace AIThread.Service.Security;

public sealed partial class SecretGuard
{
    private const long MaxLearnedFileBytes = 2_000_000;

    // The secrets of a file on disk, learned before a tool call that changes it is restored.
    private void LearnFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && info.Length <= MaxLearnedFileBytes) Learn(path, File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Unreadable: only the placeholders already known come back.
        }
    }
}
