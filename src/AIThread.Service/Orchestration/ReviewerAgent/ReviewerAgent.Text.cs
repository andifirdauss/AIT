using System.Text.Json.Nodes;

namespace AIThread.Service.Orchestration;

public sealed partial class ReviewerAgent
{
    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
