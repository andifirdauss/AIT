using System.Text.Json.Nodes;

namespace AiThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    public static bool Bool(JsonObject args, string name) =>
        args[name] is JsonValue v && (v.TryGetValue<bool>(out var b) ? b : v.TryGetValue<string>(out var s) && bool.TryParse(s, out var parsed) && parsed);
}
