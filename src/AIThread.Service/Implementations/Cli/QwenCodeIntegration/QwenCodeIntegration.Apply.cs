using System.Text.Json.Nodes;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations.Cli;

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
            _ => throw new AppValidationException(Lang.T($"modelProviders.openai di {FileName} bukan array.", $"modelProviders.openai in {FileName} is not an array."))
        };
        providers["openai"] = openAi;

        if (FindModel(root) is { } existing)
        {
            if (JsonConfigFile.GetString(existing["name"]) != DisplayName)
                throw new AppValidationException(Lang.T($"{FileName} sudah punya model \"{ModelId}\" buatan Anda di modelProviders.openai. Hapus atau ganti id-nya dulu.", $"{FileName} already has a model \"{ModelId}\" made by you in modelProviders.openai. Delete or change its id first."));
            openAi.Remove(existing);
        }
        openAi.Add(Model(entry));
        Object(root, "env")[EnvKey] = entry.ApiKey;
        return JsonConfigFile.Write(root);
    }
}
