using System.Text.Json.Nodes;

namespace AIThread.Service.Implementations.Cli;

public sealed partial class QwenCodeIntegration
{
    protected override string Preview(CliEntry entry) =>
        JsonConfigFile.Write(new JsonObject
        {
            ["modelProviders"] = new JsonObject { ["openai"] = new JsonArray(Model(entry)) },
            ["env"] = new JsonObject { [EnvKey] = entry.ApiKey }
        }).TrimEnd();
}
