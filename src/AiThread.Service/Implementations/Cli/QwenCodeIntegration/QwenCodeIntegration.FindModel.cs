using System.Text.Json.Nodes;

namespace AiThread.Service.Implementations.Cli;

public sealed partial class QwenCodeIntegration
{
    private static JsonObject? FindModel(JsonObject root) =>
        (JsonConfigFile.At(root, "modelProviders", "openai") as JsonArray)?.OfType<JsonObject>()
            .FirstOrDefault(m => JsonConfigFile.GetString(m["id"]) == ModelId);
}
