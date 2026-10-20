namespace AiThread.Service.Implementations.Cli;

public sealed partial class QwenCodeIntegration
{
    protected override string? CurrentBaseUrl(string content) =>
        JsonConfigFile.GetString(FindModel(JsonConfigFile.Read(content, FileName))?["baseUrl"]);
}
