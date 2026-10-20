using System.Text.Json.Nodes;
using AiThread.Service.Models;

namespace AiThread.Service.Implementations.Cli;

public sealed partial class QwenCodeIntegration
{
    private static JsonObject Object(JsonObject parent, string key)
    {
        switch (parent[key])
        {
            case JsonObject existing:
                return existing;
            case null:
                var created = new JsonObject();
                parent[key] = created;
                return created;
            default:
                throw new AppValidationException($"\"{key}\" di {FileName} bukan objek.");
        }
    }
}
