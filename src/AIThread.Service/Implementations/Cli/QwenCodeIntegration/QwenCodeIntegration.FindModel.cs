using System.Text.Json.Nodes;

namespace AIThread.Service.Implementations.Cli;

public sealed partial class QwenCodeIntegration
{
    private static JsonObject? FindModel(JsonObject root) =>
        (JsonConfigFile.At(root, "modelProviders", "openai") as JsonArray)?.OfType<JsonObject>()
            .FirstOrDefault(m => JsonConfigFile.GetString(m["id"]) == ModelId);
}
