using System.Text.Json.Nodes;

namespace AiThread.Service.Skills;

public sealed partial class SkillAi
{
    public static bool? Bool(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<bool>(out var b) ? b : null;
}
