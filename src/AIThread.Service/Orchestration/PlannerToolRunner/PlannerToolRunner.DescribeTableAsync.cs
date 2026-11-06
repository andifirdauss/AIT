using System.Text.Json.Nodes;
using AIThread.Common.Localization;
using AIThread.Service.Databases;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    private async Task<PlannerToolOutcome> DescribeTableAsync(PlannerInput input, JsonObject args, string prefix, ThreadRecorder recorder, ClientProgress progress,
        CancellationToken cancellationToken)
    {
        var database = FindDatabase(input, args, out var error);
        if (database is null) return new PlannerToolOutcome(error!, null, true);
        if (Str(args, "table") is not { } name) return new PlannerToolOutcome("Error: \"table\" is required.", null, true);

        // The statement reader parses the name the way the engine would (quotes, schema, case folding).
        var table = SqlStatement.Classify($"SELECT * FROM {name}", database.Provider).Tables.FirstOrDefault();
        if (table is null) return new PlannerToolOutcome($"Error: '{name}' is not a table name.", null, true);
        try
        {
            var description = await databases.DescribeTableAsync(database, table, cancellationToken);
            await progress.DetailAsync(Lang.T($"{description.Columns.Count} kolom · {description.RowCount} baris", $"{description.Columns.Count} columns · {description.RowCount} rows"),
                cancellationToken, hang: Hang(prefix));
            return new PlannerToolOutcome(DatabaseToolText.Describe(description, database), null);
        }
        catch (DatabaseToolException ex)
        {
            return new PlannerToolOutcome($"Error: {ex.Message}", null, true);
        }
    }
}
