using AIThread.Common.DTOs;

namespace AIThread.Service.CliBackend;

public sealed partial class CliModelService
{
    public async Task<IReadOnlyList<CliBackendStatusDto>> DetectAsync(bool refresh = false, CancellationToken cancellationToken = default) =>
        [await claude.DetectAsync(refresh, cancellationToken)];
}
