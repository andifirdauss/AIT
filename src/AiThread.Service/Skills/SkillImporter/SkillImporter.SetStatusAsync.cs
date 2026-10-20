using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Skills;

public sealed partial class SkillImporter
{
    private async Task<SkillSource?> SetStatusAsync(Guid sourceId, SkillSourceStatus status, string? error, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var source = await db.SkillSources.FirstOrDefaultAsync(s => s.Id == sourceId, cancellationToken);
        if (source is null) return null;

        source.Status = status;
        source.Error = error is null ? null : SkillDiscovery.Limit(error, 2000);
        source.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        eventBus.Publish(AppEvent.Create(AppEventTypes.SkillChanged, new { sourceId, status = status.ToString(), error }));
        return source;
    }
}
