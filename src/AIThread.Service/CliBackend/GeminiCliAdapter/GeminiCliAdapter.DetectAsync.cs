using AIThread.Common.DTOs;

namespace AIThread.Service.CliBackend;

public sealed partial class GeminiCliAdapter
{
    public async Task<CliBackendStatusDto> DetectAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        if (!refresh && _detected is { } cached && DateTime.UtcNow - cached.CheckedAt < DetectCacheDuration) return cached.Status;

        var status = await CliProcess.ProbeAsync(CliModelId.Gemini, DisplayName, Command, ResolveLaunch(), e => CleanEnvironment(e), logger, cancellationToken);
        _detected = new DetectCache(status, DateTime.UtcNow);
        return status;
    }
}
