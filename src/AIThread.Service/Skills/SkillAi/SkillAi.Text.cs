using System.Text.Json.Nodes;

namespace AIThread.Service.Skills;

public sealed partial class SkillAi
{
    public static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
}
