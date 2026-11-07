namespace AIThread.Service.CliBackend;

public sealed partial class AntigravityCliAdapter
{
    // Deletes the conversation agy saved for one of AIT's own runs: conversations/<id>.db (with its -wal/-shm) and
    // brain/<id>/ under the CLI's data folder. Only a GUID id is accepted, so nothing outside those two places can be
    // named. agy's own summary index (conversation_summaries.db) is left alone. Returns whether anything was deleted.
    public static bool ForgetConversation(string? conversationId, string? dataFolder = null)
    {
        if (!Guid.TryParse(conversationId, out var id)) return false;
        var root = dataFolder ?? DataFolder;
        var name = id.ToString("D");
        var deleted = false;

        foreach (var file in new[] { ".db", ".db-wal", ".db-shm" }.Select(suffix => Path.Combine(root, "conversations", name + suffix)))
        {
            try
            {
                if (!File.Exists(file)) continue;
                File.Delete(file);
                deleted = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still held by an exiting agy process; it stays, as it would without AIT.
            }
        }

        var brain = Path.Combine(root, "brain", name);
        if (Directory.Exists(brain))
        {
            CliProcess.DeleteQuietly(brain);
            deleted |= !Directory.Exists(brain);
        }
        return deleted;
    }
}
