using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Approvals;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class ApprovalService
{
    public async Task GrantAsync(Guid projectId, ApprovalKind kind, string target, Guid? threadId = null, CancellationToken cancellationToken = default)
    {
        var normalized = ApprovalTargets.Normalize(kind, target);
        if (normalized.Length > ApprovalTargets.MaxLength)
        {
            activityLog.Warn(Category, Lang.T($"Izin tidak disimpan (target lebih dari {ApprovalTargets.MaxLength} karakter); berlaku sekali: {ApprovalRequest.KindLabel(kind)}.",
                $"Permission not saved (target over {ApprovalTargets.MaxLength} characters); it counts once: {ApprovalRequest.KindLabel(kind)}."), threadId, projectId);
            return;
        }
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var grants = await db.ApprovalGrants.Where(g => g.ProjectId == projectId && g.Kind == kind).ToListAsync(cancellationToken);
        if (grants.Any(g => ApprovalTargets.Covers(kind, g.Target, normalized))) return;

        var by = (await settingService.GetRuntimeAsync(cancellationToken)).EffectiveDeveloperName;
        db.ApprovalGrants.Add(new ApprovalGrant { ProjectId = projectId, Kind = kind, Target = normalized, CreatedBy = by, LastUsedAt = DateTime.UtcNow, UseCount = 1 });
        await db.SaveChangesAsync(cancellationToken);
        activityLog.Success(Category, Lang.T($"Izin berkelanjutan disimpan: {ApprovalRequest.KindLabel(kind)} {normalized}.",
            $"Standing permission saved: {ApprovalRequest.KindLabel(kind)} {normalized}."), threadId, projectId);
        eventBus.Publish(AppEvent.Create(AppEventTypes.ApprovalChanged, new { ProjectId = projectId }));
    }
}
