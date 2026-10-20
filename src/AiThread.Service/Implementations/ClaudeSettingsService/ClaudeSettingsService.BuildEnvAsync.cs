using System.Text.Json.Nodes;

namespace AiThread.Service.Implementations;

public sealed partial class ClaudeSettingsService
{
    private async Task<JsonObject> BuildEnvAsync(string proxyBaseUrl, int? timeoutMs, string? projectRoot, CancellationToken cancellationToken)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        var env = new JsonObject
        {
            ["ANTHROPIC_BASE_URL"] = proxyBaseUrl.TrimEnd('/'),
            ["ANTHROPIC_AUTH_TOKEN"] = settings.ProxyApiKey ?? "aithread-local",
            ["API_TIMEOUT_MS"] = (timeoutMs is > 0 ? timeoutMs.Value : DefaultTimeoutMs).ToString()
        };
        if (projectRoot is not null) env["ANTHROPIC_CUSTOM_HEADERS"] = $"{ProjectHeader}: {projectRoot}";
        return env;
    }
}
