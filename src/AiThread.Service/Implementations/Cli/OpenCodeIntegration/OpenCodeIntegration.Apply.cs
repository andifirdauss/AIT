using System.Text.Json.Nodes;
using AiThread.Service.Models;

namespace AiThread.Service.Implementations.Cli;

public sealed partial class OpenCodeIntegration
{
    protected override string Apply(string content, CliEntry entry)
    {
        var root = JsonConfigFile.ReadForWrite(content, FileName);
        var providers = root["provider"] as JsonObject;
        if (root["provider"] is not null && providers is null) throw new AppValidationException($"\"provider\" di {FileName} bukan objek.");
        if (providers?[ProviderId] is { } existing && JsonConfigFile.GetString(JsonConfigFile.At(existing, "name")) != DisplayName)
            throw new AppValidationException($"{FileName} sudah punya provider \"{ProviderId}\" buatan Anda. Hapus atau ganti namanya dulu.");

        providers ??= new JsonObject();
        root["provider"] = providers;
        providers[ProviderId] = Provider(entry);
        return JsonConfigFile.Write(root);
    }
}
