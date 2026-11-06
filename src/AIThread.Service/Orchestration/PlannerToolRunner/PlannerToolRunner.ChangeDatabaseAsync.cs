using System.Text.Json.Nodes;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Approvals;
using AIThread.Service.Databases;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // backup (the restore point; nothing runs without it) → the backup file read back and checked → dry test and restore
    // test in one rolled-back transaction → the developer's permission → ExecuteAsync. Any failing step ends the call
    // with nothing executed. Dev/Staging: "always" for the database skips the card next time; Prod: always asked, with
    // a local rehearsal offered first.
    private async Task<PlannerToolOutcome> ChangeDatabaseAsync(PlannerInput input, JsonObject args, JsonObject shown, string prefix, ThreadRecorder recorder,
        ClientProgress progress, CancellationToken cancellationToken)
    {
        var hang = Hang(prefix);
        if (prefix.Length > 0) return new PlannerToolOutcome("Error: only the planner can change a database; name the change in report_findings.", null, true);
        var database = FindDatabase(input, args, out var error);
        if (database is null) return new PlannerToolOutcome(error!, null, true);
        if (!database.AllowWrites) return new PlannerToolOutcome($"Error: '{database.Name}' is read only (Project > Detail → Database). Tell the developer which change to make.", null, true);
        if (Str(args, "sql") is not { } sql) return new PlannerToolOutcome("Error: \"sql\" is required.", null, true);
        var sqliteSql = Str(args, "sqlite_sql");
        var reason = Str(args, "reason");
        var shownSql = secrets.Redact(Str(shown, "sql") ?? sql);

        var check = SqlStatement.Classify(sql, database.Provider);
        if (check.Kind != SqlStatementKind.Write || check.Target is null)
            return new PlannerToolOutcome(check.Kind == SqlStatementKind.Read
                ? "Error: this statement only reads; use query_database."
                : $"Error: the statement was refused: {check.Reason}.", null, true);
        if (database.IsProd && sqliteSql is null)
            return new PlannerToolOutcome("Error: for a Prod database also write the same change for SQLite in sqlite_sql (for the local rehearsal), then call change_database again.", null, true);

        // 1. The restore point. Without it nothing else happens.
        await progress.DetailAsync(Lang.T($"💾 backup {check.Target.Display}…", $"💾 backing up {check.Target.Display}…"), cancellationToken, hang: hang);
        TableSnapshot snapshot;
        Common.Entities.DatabaseBackup backup;
        try
        {
            snapshot = await databases.SnapshotAsync(database, check.Target, forRestore: true, cancellationToken);
            backup = await databaseBackups.SaveAsync(database, snapshot, shownSql, recorder.Thread.Id, cancellationToken);
        }
        catch (DatabaseToolException ex)
        {
            recorder.Log(ActivityLevel.Warning, Lang.T($"change_database {database.Name}: backup gagal, tidak ada yang dijalankan: {ex.Message}",
                $"change_database {database.Name}: backup failed, nothing was run: {ex.Message}"));
            await progress.DetailAsync(Lang.T($"⛔ backup gagal — tidak dijalankan: {secrets.Redact(ex.Message)}", $"⛔ backup failed — not run: {secrets.Redact(ex.Message)}"), cancellationToken, hang: hang);
            return new PlannerToolOutcome($"Error: the backup failed, so nothing was run: {ex.Message}.", null, true);
        }

        // 2. Dry test and restore test, rolled back.
        await progress.DetailAsync(Lang.T($"🧪 dry test + test restore (rollback) · backup {backup.Rows} baris", $"🧪 dry test + restore test (rolled back) · backup {backup.Rows} rows"),
            cancellationToken, hang: hang);
        DryRunResult dry;
        try
        {
            dry = await databases.DryRunAsync(database, sql, snapshot, cancellationToken);
        }
        catch (DatabaseToolException ex)
        {
            return new PlannerToolOutcome($"Error: the dry test could not run, so nothing was run: {ex.Message}.", null, true);
        }
        var dryText = DatabaseToolText.DryRun(dry, database, secrets);
        if (dry.Problem is not null || !dry.RestoreVerified)
        {
            recorder.Log(ActivityLevel.Warning, Lang.T($"change_database {database.Name}: dihentikan sebelum eksekusi — {dry.Problem}", $"change_database {database.Name}: stopped before running — {dry.Problem}"));
            await progress.DetailAsync(Lang.T($"⛔ dihentikan — {secrets.Redact(dry.Problem ?? "test restore gagal")}", $"⛔ stopped — {secrets.Redact(dry.Problem ?? "restore test failed")}"), cancellationToken, hang: hang);
            return new PlannerToolOutcome($"Error: nothing was run.\n{dryText}", null, true);
        }
        if (dry.Affected == 0)
            return new PlannerToolOutcome($"The statement affects 0 rows, so there is nothing to change (nothing was run).\n{dryText}", null);

        var change = new PendingDatabaseChange(database.Id, database.Name, sql, sqliteSql, shownSql, backup.Id, dry.Affected, check.Target, check.Tables, reason);
        var item = new ApprovalItem(ApprovalKind.DatabaseWrite, database.Name,
            $"{DatabaseToolText.EnvironmentLabel(database.Environment)} · {check.Verb} {check.Target.Display} · {dry.Affected} " + Lang.T("baris", "rows") + $" · backup {backup.Id.ToString("N")[..8]}");

        // Dev/Staging with "always" for this database: no card.
        if (!database.IsProd && await AllGrantedAsync(input, [item], recorder, cancellationToken))
            return new PlannerToolOutcome(await RunApprovedDatabaseChangeAsync(change, input, recorder, progress, cancellationToken), null);

        if (!input.CanApprove)
            return new PlannerToolOutcome($"Error: this change needs the developer's permission, and they cannot be asked from here. Nothing was run; tell them the statement in the summary.\n{dryText}", null, true);

        var rehearsed = _rehearsed.TryGetValue(RehearsalKey(recorder.Thread.Id, change), out var rehearsal) ? rehearsal : null;
        var text = Lang.T("Planner ingin mengubah data database", "The planner wants to change database rows") + (reason is null ? "." : $": {reason}") +
                   $"\n\n**{database.Name}** · {DatabaseToolText.EnvironmentLabel(database.Environment)} · {database.Display}\n```sql\n{shownSql}\n```\n" +
                   dryText +
                   Lang.T($"Backup: {backup.Rows} baris {check.Target.Display}, id `{backup.Id.ToString("N")[..8]}` (pulihkan di Project → Detail → Database).",
                       $"Backup: {backup.Rows} rows of {check.Target.Display}, id `{backup.Id.ToString("N")[..8]}` (restore in Project → Detail → Database).") +
                   (rehearsed is null ? string.Empty : "\n\n" + Lang.T("Hasil latihan lokal (SQLite): ", "Local rehearsal (SQLite): ") + rehearsed) +
                   (database.IsProd ? "\n\n" + Lang.T("⚠️ Ini database **PROD**.", "⚠️ This is a **PROD** database.") : string.Empty);
        recorder.Log(ActivityLevel.Info, Lang.T($"Planner minta izin mengubah {database.Name}: {check.Verb} {check.Target.Display}, {dry.Affected} baris (backup {backup.Id})",
            $"The planner asks permission to change {database.Name}: {check.Verb} {check.Target.Display}, {dry.Affected} rows (backup {backup.Id})"));
        await progress.DetailAsync(Lang.T("🔐 menunggu izin developer", "🔐 waiting for the developer's permission"), cancellationToken, hang: hang);
        var request = new ApprovalRequest([item], text, AllowAlways: !database.IsProd) { AllowRehearse = database.IsProd && rehearsed is null };
        var approval = new PlannerApproval(request, DatabaseChange: change);
        return new PlannerToolOutcome(string.Empty, null, Question: PlannerQuestion.ForApproval(approval));
    }
}
