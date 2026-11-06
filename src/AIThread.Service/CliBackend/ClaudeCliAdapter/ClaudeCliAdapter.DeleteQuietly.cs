namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    private static void DeleteQuietly(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file still held by the exiting process. It only holds the system prompt, already masked by SecretGuard.
        }
    }
}
