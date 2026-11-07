using AIThread.Common.DTOs;

namespace AIThread.Service.CliBackend;

public sealed partial class CodexCliAdapter
{
    public async Task<CliBackendStatusDto> DetectAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        if (!refresh && _detected is { } cached && DateTime.UtcNow - cached.CheckedAt < DetectCacheDuration) return cached.Status;

        var status = await CliProcess.ProbeAsync(CliModelId.Codex, DisplayName, Command, ResolveLaunch(), CleanEnvironment, logger, cancellationToken);
        _detected = new DetectCache(status, DateTime.UtcNow);
        return status;
    }
}
