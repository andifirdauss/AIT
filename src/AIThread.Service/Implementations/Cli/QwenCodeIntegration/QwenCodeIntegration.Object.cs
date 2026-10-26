using System.Text.Json.Nodes;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations.Cli;

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
                throw new AppValidationException(Lang.T($"\"{key}\" di {FileName} bukan objek.", $"\"{key}\" in {FileName} is not an object."));
        }
    }
}
