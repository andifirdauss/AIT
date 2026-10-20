using System.Text.Json.Nodes;

namespace AIThread.Service.Implementations.Cli;

public sealed partial class QwenCodeIntegration
{
    private static JsonObject Model(CliEntry entry) => new()
    {
        ["id"] = ModelId,
        ["name"] = DisplayName,
        ["description"] = "Orkestrasi AIT (model asli dipilih AIT)",
        ["envKey"] = EnvKey,
        ["baseUrl"] = entry.BaseUrl,
        ["generationConfig"] = new JsonObject
        {
            ["timeout"] = 1_800_000,
            ["contextWindowSize"] = 1_000_000,
            ["modalities"] = new JsonObject { ["image"] = true }
        }
    };
}
