using System.Text.Json.Nodes;

namespace AiThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    public static string? Str(JsonObject args, string name) =>
        args[name] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;
}
