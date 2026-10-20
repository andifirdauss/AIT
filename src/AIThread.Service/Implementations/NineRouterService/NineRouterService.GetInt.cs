using System.Text.Json.Nodes;

namespace AIThread.Service.Implementations;

public sealed partial class NineRouterService
{
    private static int? GetInt(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<int>(out var i)) return i;
        if (value.TryGetValue<long>(out var l)) return (int)Math.Clamp(l, int.MinValue, int.MaxValue);
        if (value.TryGetValue<double>(out var d)) return (int)d;
        if (value.TryGetValue<string>(out var s) && int.TryParse(s, out var parsed)) return parsed;
        return null;
    }
}
