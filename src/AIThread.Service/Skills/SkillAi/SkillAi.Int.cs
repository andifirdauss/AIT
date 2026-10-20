using System.Text.Json.Nodes;

namespace AIThread.Service.Skills;

public sealed partial class SkillAi
{
    public static int? Int(JsonNode? node) =>
        node is JsonValue value ? value.TryGetValue<int>(out var i) ? i : value.TryGetValue<double>(out var d) ? (int)d : null : null;
}
