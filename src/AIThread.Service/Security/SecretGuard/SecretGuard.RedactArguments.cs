using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIThread.Service.Security;

public sealed partial class SecretGuard
{
    // Tool call arguments are JSON text, often with more JSON inside (a written appsettings.json, its quotes as \" or
    // "). Masked as plain text, a rule matches across those escapes ("Password=abc\"" takes the backslash): the JSON
    // breaks, and 9router hands the provider a string where tool_use.input must be an object, or the value swallows the
    // rest of the file. So every string value is masked on its own, and the key it sits under stands in for the
    // "password": "…" rule. Text that is not JSON (a cut-off call) is masked as it is.
    private string RedactArguments(string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return Redact(json);
        }
        if (node is null) return json;

        var changed = false;
        Walk(node, null);
        return changed ? node.ToJsonString(ArgumentsJson) : json;

        void Walk(JsonNode current, string? parent)
        {
            switch (current)
            {
                case JsonObject obj:
                    foreach (var key in obj.Select(p => p.Key).ToList())
                    {
                        if (obj[key] is JsonValue value && value.TryGetValue<string>(out var text)) obj[key] = Masked(text, Qualified(key, parent));
                        else if (obj[key] is { } child) Walk(child, key);
                    }
                    break;
                case JsonArray array:
                    for (var i = 0; i < array.Count; i++)
                    {
                        if (array[i] is JsonValue value && value.TryGetValue<string>(out var text)) array[i] = Masked(text, parent);
                        else if (array[i] is { } child) Walk(child, parent);
                    }
                    break;
            }
        }

        string Masked(string text, string? key)
        {
            var masked = Redact(text);
            if (string.Equals(masked, text, StringComparison.Ordinal) && key is not null && SecretRules.IsSecretKey(key)
                && text.Length is >= SecretRules.MinValueChars and <= SecretRules.MaxValueChars && !SecretRules.IsPlaceholderLike(text))
                masked = Mask(key, text, count: true);
            if (!string.Equals(masked, text, StringComparison.Ordinal)) changed = true;
            return masked;
        }

        // A bare "Key" names the secret through the object around it: "Jwt": { "Key": "…" } reads as "Jwt:Key".
        static string Qualified(string key, string? parent) =>
            parent is not null && (key.Equals("key", StringComparison.OrdinalIgnoreCase) || key.Equals("keys", StringComparison.OrdinalIgnoreCase))
                ? $"{parent}:{key}"
                : key;
    }
}
