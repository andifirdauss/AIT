using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Approvals;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class ApprovalService
{
    public async Task<bool> UseAsync(Guid projectId, ApprovalKind kind, string target, Guid? threadId = null, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var grants = await db.ApprovalGrants.Where(g => g.ProjectId == projectId && g.Kind == kind).ToListAsync(cancellationToken);
        var grant = grants.FirstOrDefault(g => ApprovalTargets.Covers(kind, g.Target, target));
        if (grant is null) return false;

        grant.UseCount++;
        grant.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        activityLog.Info(Category, Lang.T($"Izin tersimpan dipakai: {ApprovalRequest.KindLabel(kind)} {ApprovalTargets.Normalize(kind, target)}.",
            $"Saved permission used: {ApprovalRequest.KindLabel(kind)} {ApprovalTargets.Normalize(kind, target)}."), threadId, projectId);
        return true;
    }
}
