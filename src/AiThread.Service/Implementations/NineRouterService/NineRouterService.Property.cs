using System.Text.Json.Nodes;

namespace AiThread.Service.Implementations;

public sealed partial class NineRouterService
{
    private static JsonNode? Property(JsonNode? node, string name) =>
        node is JsonObject obj ? obj[name] : null;
}
