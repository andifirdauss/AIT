namespace AIThread.Service.Implementations.Cli;

public sealed partial class OpenCodeIntegration
{
    protected override string? CurrentBaseUrl(string content) =>
        JsonConfigFile.GetString(JsonConfigFile.At(JsonConfigFile.Read(content, FileName), "provider", ProviderId, "options", "baseURL"));
}
