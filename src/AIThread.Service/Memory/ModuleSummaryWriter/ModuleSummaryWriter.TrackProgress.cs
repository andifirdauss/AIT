using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Memory;

public sealed partial class ModuleSummaryWriter
{
    // Starts the count when facts first wait, raises the total when more arrive than it allows for, and clears it once
    // nothing waits (a later import starts a new count). Returns the progress to show.
    private MigrationProgress? TrackProgress(int waiting)
    {
        var progress = ReadProgress();
        var next = waiting == 0 ? null
            : progress is null ? new MigrationProgress(waiting, DateTime.UtcNow)
            : waiting > progress.Total ? progress with { Total = waiting }
            : progress;
        if (next == progress) return progress;
        try
        {
            if (next is null) File.Delete(ProgressFile);
            else
            {
                Directory.CreateDirectory(MemoryRelabeler.BackupDirectory);
                File.WriteAllBytes(ProgressFile + ".tmp", JsonSerializer.SerializeToUtf8Bytes(next, BackupJson));
                File.Move(ProgressFile + ".tmp", ProgressFile, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not save the change log migration progress.");
        }
        return next;
    }
}
