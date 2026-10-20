using System.Text.Json.Nodes;

namespace AiThread.Service.Implementations.Cli;

public sealed partial class QwenCodeIntegration
{
    protected override string Remove(string content)
    {
        var lenient = JsonConfigFile.Read(content, FileName);
        if (FindModel(lenient) is null && JsonConfigFile.At(lenient, "env", EnvKey) is null) return content;

        var root = JsonConfigFile.ReadForWrite(content, FileName);
        if (FindModel(root) is { } model && JsonConfigFile.At(root, "modelProviders", "openai") is JsonArray openAi)
        {
            openAi.Remove(model);
            var providers = (JsonObject)root["modelProviders"]!;
            if (openAi.Count == 0) providers.Remove("openai");
            if (providers.Count == 0) root.Remove("modelProviders");
        }
        if (root["env"] is JsonObject env)
        {
            env.Remove(EnvKey);
            if (env.Count == 0) root.Remove("env");
        }
        return root.Count == 0 ? string.Empty : JsonConfigFile.Write(root);
    }
}
