using System.Text.Json.Nodes;

namespace AIThread.Service.Security;

public sealed partial class SecretGuard
{
    // Tool arguments (tester tools, Claude Code's edit tools): every string value restored in place. filePath: the file
    // the call changes; its current secrets are learned first, so placeholders copied from it come back even after a
    // restart. Text is the restored JSON.
    public SecretRestore RestoreJson(JsonNode? node, string? filePath = null)
    {
        if (node is null) return new SecretRestore(string.Empty, 0, []);
        if (!string.IsNullOrWhiteSpace(filePath)) LearnFile(filePath);

        var restored = 0;
        var unknown = new List<string>();
        Walk(node);
        return new SecretRestore(node.ToJsonString(), restored, unknown);

        void Walk(JsonNode current)
        {
            switch (current)
            {
                case JsonObject obj:
                    foreach (var key in obj.Select(p => p.Key).ToList())
                    {
                        if (obj[key] is JsonValue value && value.TryGetValue<string>(out var text)) obj[key] = Restored(text);
                        else if (obj[key] is { } child) Walk(child);
                    }
                    break;
                case JsonArray array:
                    for (var i = 0; i < array.Count; i++)
                    {
                        if (array[i] is JsonValue value && value.TryGetValue<string>(out var text)) array[i] = Restored(text);
                        else if (array[i] is { } child) Walk(child);
                    }
                    break;
            }
        }

        JsonNode Restored(string text)
        {
            var result = Restore(text);
            restored += result.Restored;
            unknown.AddRange(result.Unknown);
            return JsonValue.Create(result.Text);
        }
    }
}
