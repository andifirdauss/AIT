using System.Text.Json.Nodes;
using AiThread.Service.Skills;

namespace AiThread.Service.Memory;

public sealed partial class MemoryAi
{
    public static IReadOnlyList<string> Strings(JsonNode? node) => node switch
    {
        JsonArray array => array.Select(SkillAi.Text).OfType<string>().ToList(),
        JsonValue => SkillAi.Text(node) is { } text ? text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [],
        _ => []
    };
}
