using System.Text.Json.Nodes;

namespace AiThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    public static int? Int(JsonObject args, string name)
    {
        if (args[name] is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<double>(out var d)) return (int)d;
        return v.TryGetValue<string>(out var s) && int.TryParse(s, out var parsed) ? parsed : null;
    }
}
