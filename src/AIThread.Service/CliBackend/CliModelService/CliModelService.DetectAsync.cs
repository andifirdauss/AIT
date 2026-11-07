using AIThread.Common.DTOs;

namespace AIThread.Service.CliBackend;

public sealed partial class CliModelService
{
    // Every engine, installed or not, checked in parallel (each `--version` can take seconds).
    public async Task<IReadOnlyList<CliBackendStatusDto>> DetectAsync(bool refresh = false, CancellationToken cancellationToken = default) =>
        await Task.WhenAll(_engines.Select(e => e.DetectAsync(refresh, cancellationToken)));
}
