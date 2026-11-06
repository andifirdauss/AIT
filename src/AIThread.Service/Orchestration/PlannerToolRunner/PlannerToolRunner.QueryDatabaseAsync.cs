using System.Text.Json.Nodes;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Databases;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // A read on a project database. The rows go back with sensitive columns as placeholders; the log keeps the statement
    // as the model wrote it (placeholders, not the values they stand for).
    private async Task<PlannerToolOutcome> QueryDatabaseAsync(PlannerInput input, JsonObject args, JsonObject shown, string prefix, ThreadRecorder recorder,
        ClientProgress progress, CancellationToken cancellationToken)
    {
        var database = FindDatabase(input, args, out var error);
        if (database is null) return new PlannerToolOutcome(error!, null, true);
        if (Str(args, "sql") is not { } sql) return new PlannerToolOutcome("Error: \"sql\" is required.", null, true);
        var maxRows = Math.Clamp(Int(args, "max_rows") ?? database.MaxRows, 1, Implementations.ProjectDatabaseService.MaxRowsLimit);

        try
        {
            var result = await databases.QueryAsync(database, sql, maxRows, cancellationToken);
            recorder.Log(ActivityLevel.Debug, $"query_database {database.Name}: {secrets.Redact(Str(shown, "sql") ?? string.Empty)} → {result.Rows.Count} rows");
            await progress.DetailAsync(Lang.T($"{result.Rows.Count} baris{(result.Truncated ? " (dipotong)" : string.Empty)} · {result.Elapsed.TotalMilliseconds:0} ms",
                $"{result.Rows.Count} rows{(result.Truncated ? " (cut)" : string.Empty)} · {result.Elapsed.TotalMilliseconds:0} ms"), cancellationToken, hang: Hang(prefix));
            return new PlannerToolOutcome(DatabaseToolText.Rows(result, database, secrets), null);
        }
        catch (DatabaseToolException ex)
        {
            await progress.DetailAsync($"⛔ {secrets.Redact(ex.Message)}", cancellationToken, hang: Hang(prefix));
            return new PlannerToolOutcome($"Error: {ex.Message}", null, true);
        }
    }
}
