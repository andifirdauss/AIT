using System.Text.Json.Nodes;

namespace AiThread.Service.Implementations.Cli;

public sealed partial class OpenCodeIntegration
{
    protected override string Remove(string content)
    {
        if (JsonConfigFile.At(JsonConfigFile.Read(content, FileName), "provider", ProviderId) is null) return content;
        var root = JsonConfigFile.ReadForWrite(content, FileName);
        var providers = (JsonObject)root["provider"]!;
        providers.Remove(ProviderId);
        if (providers.Count == 0) root.Remove("provider");
        return root.Count == 0 ? string.Empty : JsonConfigFile.Write(root);
    }
}
