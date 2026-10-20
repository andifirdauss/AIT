using System.Text.Json.Nodes;
using AiThread.Service.Models;

namespace AiThread.Service.Implementations.Cli;

public sealed partial class QwenCodeIntegration
{
    protected override string Apply(string content, CliEntry entry)
    {
        var root = JsonConfigFile.ReadForWrite(content, FileName);
        var providers = Object(root, "modelProviders");
        var openAi = providers["openai"] switch
        {
            null => new JsonArray(),
            JsonArray array => array,
            _ => throw new AppValidationException($"modelProviders.openai di {FileName} bukan array.")
        };
        providers["openai"] = openAi;

        if (FindModel(root) is { } existing)
        {
            if (JsonConfigFile.GetString(existing["name"]) != DisplayName)
                throw new AppValidationException($"{FileName} sudah punya model \"{ModelId}\" buatan Anda di modelProviders.openai. Hapus atau ganti id-nya dulu.");
            openAi.Remove(existing);
        }
        openAi.Add(Model(entry));
        Object(root, "env")[EnvKey] = entry.ApiKey;
        return JsonConfigFile.Write(root);
    }
}
