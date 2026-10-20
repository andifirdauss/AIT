using System.Text.Json.Nodes;

namespace AiThread.Service.Implementations.Cli;

public sealed partial class OpenCodeIntegration
{
    protected override string Preview(CliEntry entry) =>
        JsonConfigFile.Write(new JsonObject { ["provider"] = new JsonObject { [ProviderId] = Provider(entry) } }).TrimEnd();
}
