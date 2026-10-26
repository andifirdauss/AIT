using System.Text.Json.Nodes;
using System.Text.Json;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

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
                   ?? throw new AppValidationException(Lang.T($"{path} bukan objek JSON.", $"{path} is not a JSON object."));
        }
        catch (JsonException ex)
        {
            throw new AppValidationException(Lang.T($"{path} tidak dapat dibaca sebagai JSON: {ex.Message}", $"{path} cannot be read as JSON: {ex.Message}"));
        }
    }
}
