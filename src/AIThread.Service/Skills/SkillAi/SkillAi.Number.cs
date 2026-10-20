using System.Text.Json.Nodes;

namespace AIThread.Service.Skills;

public sealed partial class SkillAi
{
    public static double? Number(JsonNode? node) =>
        node is JsonValue value ? value.TryGetValue<double>(out var d) ? d : value.TryGetValue<string>(out var s) && double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : null : null;
}
