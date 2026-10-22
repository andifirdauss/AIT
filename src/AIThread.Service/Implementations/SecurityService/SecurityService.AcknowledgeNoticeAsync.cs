using AIThread.Common.DTOs;

namespace AIThread.Service.Implementations;

public sealed partial class SecurityService
{
    // Once per installation: the notice no longer opens by itself, in any browser.
    public async Task<SecurityStatusDto> AcknowledgeNoticeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await LoadTrackedAsync(db, cancellationToken);
        entity.PrivacyNoticeAcknowledgedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(entity);
    }
}
