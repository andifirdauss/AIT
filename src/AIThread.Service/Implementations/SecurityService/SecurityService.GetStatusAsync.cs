using AIThread.Common.DTOs;

namespace AIThread.Service.Implementations;

public sealed partial class SecurityService
{
    public async Task<SecurityStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return ToDto(await LoadTrackedAsync(db, cancellationToken));
    }
}
