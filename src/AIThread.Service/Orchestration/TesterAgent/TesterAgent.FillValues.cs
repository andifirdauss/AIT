using System.Text.Json.Nodes;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    // A copy of the tool arguments with every remembered {{name}} replaced, whatever the JSON nesting.
    private static JsonNode? FillValues(JsonNode? node, IReadOnlyDictionary<string, string> values) => node switch
    {
        JsonObject obj => new JsonObject(obj.Select(p => KeyValuePair.Create(p.Key, FillValues(p.Value, values)))),
        JsonArray array => new JsonArray(array.Select(n => FillValues(n, values)).ToArray()),
        JsonValue value when value.TryGetValue<string>(out var text) && text.Contains("{{", StringComparison.Ordinal) =>
            JsonValue.Create(values.Aggregate(text, (current, pair) => current.Replace($"{{{{{pair.Key}}}}}", pair.Value, StringComparison.OrdinalIgnoreCase))),
        _ => node?.DeepClone()
    };
}
