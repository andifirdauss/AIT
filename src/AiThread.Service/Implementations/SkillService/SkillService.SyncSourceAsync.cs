using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class SkillService
{
    public async Task<SkillSourceDto?> SyncSourceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var source = await db.SkillSources.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (source is null) return null;
        if (source.Status is SkillSourceStatus.Queued or SkillSourceStatus.Fetching or SkillSourceStatus.Analyzing)
            throw new AppValidationException("Sumber ini sedang dalam antrean atau sedang ditarik.");

        source.Status = SkillSourceStatus.Queued;
        source.Error = null;
        source.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        queue.Enqueue(source.Id);
        eventBus.Publish(AppEvent.Create(AppEventTypes.SkillChanged, new { sourceId = source.Id, status = source.Status.ToString() }));
        return ToDto(source);
    }
}
