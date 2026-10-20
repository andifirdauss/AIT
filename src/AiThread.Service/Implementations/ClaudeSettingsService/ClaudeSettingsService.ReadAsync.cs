using System.Text.Json;
using System.Text.Json.Nodes;
using AiThread.Service.Models;

namespace AiThread.Service.Implementations;

public sealed partial class ClaudeSettingsService
{
    private static async Task<JsonObject> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return new JsonObject();
        var text = await File.ReadAllTextAsync(path, cancellationToken);
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();

        try
        {
            return JsonNode.Parse(text, documentOptions: ReadOptions) as JsonObject
                   ?? throw new AppValidationException($"{path} bukan objek JSON.");
        }
        catch (JsonException ex)
        {
            throw new AppValidationException($"{path} tidak dapat dibaca sebagai JSON: {ex.Message}");
        }
    }
}
