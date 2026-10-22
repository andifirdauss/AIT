using System.Text.Json.Nodes;

namespace AIThread.Service.Security;

public sealed partial class SecretGuard
{
    // Fields of a client request that must reach the provider unchanged: signed or encrypted reasoning, base64 media,
    // ids, names and tool schemas.
    private static readonly HashSet<string> UntouchedFields = new(StringComparer.Ordinal)
    {
        "thinking", "signature", "data", "encrypted_content", "thought_signature", "thoughtSignature", "id", "tool_use_id",
        "tool_call_id", "call_id", "model", "type", "role", "media_type", "mime_type", "mimeType", "name", "tools",
        "metadata", "input_schema", "parameters", "cache_control", "inlineData", "fileData", "image_url", "source"
    };

    // A request forwarded to 9router as it is (passthrough), in any client format: every other string masked in place.
    // Returns how many strings changed.
    public int RedactJson(JsonNode? node)
    {
        if (!_enabled || node is null) return 0;
        var changed = 0;
        Walk(node);
        return changed;

        void Walk(JsonNode current)
        {
            switch (current)
            {
                case JsonObject obj:
                    foreach (var key in obj.Select(p => p.Key).ToList())
                    {
                        if (UntouchedFields.Contains(key)) continue;
                        if (obj[key] is JsonValue value && value.TryGetValue<string>(out var text)) obj[key] = Masked(text);
                        else if (obj[key] is { } child) Walk(child);
                    }
                    break;
                case JsonArray array:
                    for (var i = 0; i < array.Count; i++)
                    {
                        if (array[i] is JsonValue value && value.TryGetValue<string>(out var text)) array[i] = Masked(text);
                        else if (array[i] is { } child) Walk(child);
                    }
                    break;
            }
        }

        JsonNode Masked(string text)
        {
            var masked = Redact(text);
            if (!string.Equals(masked, text, StringComparison.Ordinal)) changed++;
            return JsonValue.Create(masked);
        }
    }
}
