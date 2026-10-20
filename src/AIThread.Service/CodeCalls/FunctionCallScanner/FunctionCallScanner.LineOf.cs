using System.Text.Json.Nodes;
using AIThread.Service.Skills;

namespace AIThread.Service.CodeCalls;

public sealed partial class FunctionCallScanner
{
    // A number, or a number written as text ("26").
    private static int LineOf(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<int>(out var number) ? number
        : int.TryParse(SkillAi.Text(node), out var parsed) ? parsed
        : 0;
}
