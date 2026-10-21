using System.Text.Json.Nodes;
using AIThread.Service.Testing;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    // A copy of the tool arguments with {{username}} and {{password}} replaced in every string (a fill text, a request
    // body or header), whatever the JSON nesting.
    private static JsonNode? Fill(JsonNode? node, TestLogin login) => node switch
    {
        JsonObject obj => new JsonObject(obj.Select(p => KeyValuePair.Create(p.Key, Fill(p.Value, login)))),
        JsonArray array => new JsonArray(array.Select(n => Fill(n, login)).ToArray()),
        JsonValue value when value.TryGetValue<string>(out var text) => JsonValue.Create(login.Fill(text)),
        _ => node?.DeepClone()
    };
}
