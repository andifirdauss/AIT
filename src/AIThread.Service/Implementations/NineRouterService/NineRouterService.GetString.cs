using System.Text.Json.Nodes;

namespace AIThread.Service.Implementations;

public sealed partial class NineRouterService
{
    private static string? GetString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
