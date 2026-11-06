using System.Text.Json.Nodes;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Databases;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // Committed only when the statement affects exactly the dry test's row count; the backup stays as the restore point.
    public async Task<string> RunApprovedDatabaseChangeAsync(PendingDatabaseChange change, PlannerInput input, ThreadRecorder recorder, ClientProgress progress,
        CancellationToken cancellationToken)
    {
        await AnnounceAsync(PlannerTools.ChangeDatabase, new JsonObject { ["database"] = change.DatabaseName, ["sql"] = change.Shown }, "Planner", string.Empty, recorder, progress,
            cancellationToken);
        var database = (input.Databases ?? []).FirstOrDefault(d => d.Id == change.DatabaseId);
        if (database is null) return $"Error: the database '{change.DatabaseName}' is no longer set up; nothing was run.";

        try
        {
            var affected = await databases.ExecuteAsync(database, change.Sql, change.ExpectedRows, cancellationToken);
            await databaseBackups.MarkExecutedAsync(change.BackupId, affected, cancellationToken);
            var backupId = change.BackupId.ToString("N")[..8];
            recorder.Log(ActivityLevel.Success, Lang.T($"Database {database.Name} diubah: {affected} baris di {change.Target.Display} (backup {backupId} bisa memulihkannya).",
                $"Database {database.Name} changed: {affected} rows in {change.Target.Display} (backup {backupId} can restore it)."));
            await progress.DetailAsync(Lang.T($"✅ {affected} baris diubah · backup {backupId}", $"✅ {affected} rows changed · backup {backupId}"), cancellationToken, hang: Hang(string.Empty));
            return $"Committed: {affected} row(s) of {change.Target.Display} on {database.Name} changed, the same count as the dry test. Backup {backupId} restores the table as it was (Project > Detail → Database); mention it in the summary.";
        }
        catch (DatabaseToolException ex)
        {
            recorder.Log(ActivityLevel.Error, Lang.T($"Perubahan {database.Name} gagal dan di-rollback: {ex.Message}", $"The change on {database.Name} failed and was rolled back: {ex.Message}"));
            await progress.DetailAsync(Lang.T($"⛔ gagal, di-rollback: {secrets.Redact(ex.Message)}", $"⛔ failed, rolled back: {secrets.Redact(ex.Message)}"), cancellationToken, hang: Hang(string.Empty));
            return $"Error: {ex.Message}. Nothing was committed.";
        }
    }
}
