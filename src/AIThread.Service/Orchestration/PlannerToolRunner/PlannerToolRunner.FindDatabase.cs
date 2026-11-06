using System.Text.Json.Nodes;
using AIThread.Service.Databases;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    private static DatabaseTarget? FindDatabase(PlannerInput input, JsonObject args, out string? error) =>
        DatabaseLookup.Find(input.Databases, Str(args, "database"), out error);
}
