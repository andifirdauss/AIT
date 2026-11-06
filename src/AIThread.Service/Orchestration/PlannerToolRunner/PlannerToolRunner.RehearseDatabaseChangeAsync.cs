using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Databases;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // The tables the statement names (the changed one first, at most five) are copied from the target into a local SQLite
    // file, sqlite_sql runs there and is compared with the target's dry test. The planner gets the result and asks again
    // with change_database; that card shows the rehearsal and offers no second one.
    public async Task<string> RehearseDatabaseChangeAsync(PendingDatabaseChange change, PlannerInput input, ThreadRecorder recorder, ClientProgress progress,
        CancellationToken cancellationToken)
    {
        var database = (input.Databases ?? []).FirstOrDefault(d => d.Id == change.DatabaseId);
        if (database is null) return $"Error: the database '{change.DatabaseName}' is no longer set up; nothing was run.";
        if (change.SqliteSql is null) return "Error: there is no sqlite_sql to rehearse. Call change_database again with sqlite_sql.";

        await progress.StepAsync(Lang.T($"🧪 Latihan lokal: salin {change.Target.Display} dari {database.Name} ke SQLite", $"🧪 Local rehearsal: copying {change.Target.Display} from {database.Name} to SQLite"),
            cancellationToken);
        var tables = new[] { change.Target }.Concat(change.Tables.Where(t => t != change.Target)).Take(MaxRehearsalTables).ToList();
        var copies = new List<TableSnapshot>();
        try
        {
            foreach (var table in tables) copies.Add(await databases.SnapshotAsync(database, table, forRestore: table == change.Target, cancellationToken));
        }
        catch (DatabaseToolException ex)
        {
            return $"Error: the tables could not be copied for the rehearsal: {ex.Message}. Nothing was run on {database.Name}. Ask the developer whether to run it without a rehearsal (call change_database again).";
        }

        var result = await databases.RehearseAsync(copies, change.Target, change.SqliteSql, recorder.Thread.Id, cancellationToken);
        if (result.Problem is not null)
        {
            recorder.Log(ActivityLevel.Warning, Lang.T($"Latihan lokal {database.Name} gagal: {result.Problem}", $"Local rehearsal on {database.Name} failed: {result.Problem}"));
            return $"Error: the rehearsal could not run: {result.Problem}. Nothing was run on {database.Name}. Fix sqlite_sql and call change_database again.";
        }

        var match = result.Affected == change.ExpectedRows;
        var summary = Lang.T($"{result.Affected} baris terpengaruh di salinan lokal (dry test {database.Name}: {change.ExpectedRows}){(match ? string.Empty : " — BERBEDA")}.",
            $"{result.Affected} rows affected on the local copy (dry test on {database.Name}: {change.ExpectedRows}){(match ? string.Empty : " — DIFFERENT")}.");
        _rehearsed[RehearsalKey(recorder.Thread.Id, change)] = summary + Lang.T(" Latihan di SQLite hanya mendekati perilaku database target.", " A SQLite rehearsal only approximates the target database.");
        recorder.Log(match ? ActivityLevel.Info : ActivityLevel.Warning, Lang.T($"Latihan lokal {database.Name}: {summary}", $"Local rehearsal on {database.Name}: {summary}"));
        await progress.DetailAsync(match ? $"✅ {summary}" : $"⚠️ {summary}", cancellationToken, hang: Hang(string.Empty));

        var sample = DatabaseToolText.DryRun(new DryRunResult(result.Affected, result.Sample, result.ChangedRows, new RestoreCounts(0, 0, 0), true, null), database, secrets)
            .Split('\n').Where(l => !l.StartsWith("Restore test", StringComparison.Ordinal)).ToArray();
        return $"Rehearsal on a local SQLite copy (deleted afterwards): {result.Affected} row(s) affected, the dry test on {database.Name} affected {change.ExpectedRows}.\n" +
               string.Join("\n", sample).Trim() + "\n" +
               (match
                   ? "If this is the intended change, call change_database again with the same sql and sqlite_sql: the developer is then asked to run it on Prod."
                   : "The counts differ: check both statements (the SQLite version may not match the target dialect) before calling change_database again.") +
               " Nothing was run on the target database.";
    }
}
