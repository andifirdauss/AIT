using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class SkillService
{
    public async Task<SkillSourceDto?> SyncSourceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var source = await db.SkillSources.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (source is null) return null;
        if (source.Status is SkillSourceStatus.Queued or SkillSourceStatus.Fetching or SkillSourceStatus.Analyzing)
            throw new AppValidationException(Lang.T("Sumber ini sedang dalam antrean atau sedang ditarik.", "This source is queued or being fetched."));

        source.Status = SkillSourceStatus.Queued;
        source.Error = null;
        source.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        queue.Enqueue(source.Id);
        eventBus.Publish(AppEvent.Create(AppEventTypes.SkillChanged, new { sourceId = source.Id, status = source.Status.ToString() }));
        return ToDto(source);
    }
}
