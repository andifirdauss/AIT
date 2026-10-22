using System.Text.Json.Nodes;

namespace AIThread.Service.Security;

public sealed partial class SecretGuard
{
    // An OpenAI-style message list just before it goes to 9router, changed in place (the caller keeps appending to it,
    // so later calls find the earlier turns already masked): text content, text parts and tool call arguments.
    public void RedactMessages(JsonArray messages)
    {
        if (!_enabled) return;
        foreach (var message in messages.OfType<JsonObject>())
        {
            switch (message["content"])
            {
                case JsonValue value when value.TryGetValue<string>(out var text):
                    message["content"] = Redact(text);
                    break;
                case JsonArray parts:
                    foreach (var part in parts.OfType<JsonObject>())
                        if (part["text"] is JsonValue partValue && partValue.TryGetValue<string>(out var partText))
                            part["text"] = Redact(partText);
                    break;
            }

            if (message["tool_calls"] is not JsonArray calls) continue;
            foreach (var function in calls.OfType<JsonObject>().Select(c => c["function"]).OfType<JsonObject>())
                if (function["arguments"] is JsonValue arguments && arguments.TryGetValue<string>(out var json))
                    function["arguments"] = Redact(json);
        }
    }
}
