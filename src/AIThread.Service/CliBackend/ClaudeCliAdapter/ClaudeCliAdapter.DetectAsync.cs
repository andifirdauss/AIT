using AIThread.Common.DTOs;

namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    // `claude --version` only: whether it is logged in shows on the first call (the node test), not here, since asking
    // would spend a request on the account.
    public async Task<CliBackendStatusDto> DetectAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        if (!refresh && _detected is { } cached && DateTime.UtcNow - cached.CheckedAt < DetectCacheDuration) return cached.Status;

        var status = await ProbeAsync(cancellationToken);
        _detected = new DetectCache(status, DateTime.UtcNow);
        return status;
    }
}
