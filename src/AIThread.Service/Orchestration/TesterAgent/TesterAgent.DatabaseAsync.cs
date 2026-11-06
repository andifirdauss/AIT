using System.Text.Json.Nodes;
using AIThread.Service.Databases;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    // The tester's database tools: reads only, sensitive columns as placeholders (run.Mask masks the rest).
    private async Task<string> DatabaseAsync(string tool, JsonObject args, TestRun run, CancellationToken cancellationToken)
    {
        if (tool == PlannerTools.ListDatabases) return DatabaseToolText.Databases(run.Databases);

        var database = DatabaseLookup.Find(run.Databases, PlannerToolRunner.Str(args, "database"), out var error);
        if (database is null) return error!;
        try
        {
            if (tool == PlannerTools.DescribeTable)
            {
                var name = PlannerToolRunner.Str(args, "table");
                var table = name is null ? null : SqlStatement.Classify($"SELECT * FROM {name}", database.Provider).Tables.FirstOrDefault();
                if (table is null) return "Error: \"table\" must be a table name.";
                return DatabaseToolText.Describe(await databaseQueries.DescribeTableAsync(database, table, cancellationToken), database);
            }

            if (PlannerToolRunner.Str(args, "sql") is not { } sql) return "Error: \"sql\" is required.";
            var maxRows = Math.Clamp(PlannerToolRunner.Int(args, "max_rows") ?? database.MaxRows, 1, Implementations.ProjectDatabaseService.MaxRowsLimit);
            return DatabaseToolText.Rows(await databaseQueries.QueryAsync(database, sql, maxRows, cancellationToken), database, secrets);
        }
        catch (DatabaseToolException ex)
        {
            return $"Error: {ex.Message}";
        }
    }
}
