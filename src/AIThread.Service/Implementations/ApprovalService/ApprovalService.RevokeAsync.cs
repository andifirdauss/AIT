using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Approvals;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class ApprovalService
{
    public async Task<bool> RevokeAsync(Guid projectId, Guid grantId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var grant = await db.ApprovalGrants.FirstOrDefaultAsync(g => g.Id == grantId && g.ProjectId == projectId, cancellationToken);
        if (grant is null) return false;

        db.ApprovalGrants.Remove(grant);
        await db.SaveChangesAsync(cancellationToken);
        activityLog.Info(Category, Lang.T($"Izin berkelanjutan dicabut: {ApprovalRequest.KindLabel(grant.Kind)} {grant.Target}.",
            $"Standing permission revoked: {ApprovalRequest.KindLabel(grant.Kind)} {grant.Target}."), projectId: projectId);
        eventBus.Publish(AppEvent.Create(AppEventTypes.ApprovalChanged, new { ProjectId = projectId }));
        return true;
    }
}
