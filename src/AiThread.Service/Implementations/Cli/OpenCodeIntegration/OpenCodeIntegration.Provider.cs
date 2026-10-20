using System.Text.Json.Nodes;

namespace AiThread.Service.Implementations.Cli;

public sealed partial class OpenCodeIntegration
{
    private static JsonObject Provider(CliEntry entry) => new()
    {
        ["npm"] = "@ai-sdk/anthropic",
        ["name"] = DisplayName,
        ["options"] = new JsonObject
        {
            ["baseURL"] = entry.BaseUrl,
            ["apiKey"] = entry.ApiKey,
            ["timeout"] = false
        },
        ["models"] = new JsonObject
        {
            [ModelId] = new JsonObject
            {
                ["name"] = DisplayName,
                ["modalities"] = new JsonObject { ["input"] = new JsonArray("text", "image"), ["output"] = new JsonArray("text") },
                ["limit"] = new JsonObject { ["context"] = 1_000_000, ["output"] = 32_000 }
            }
        }
    };
}
