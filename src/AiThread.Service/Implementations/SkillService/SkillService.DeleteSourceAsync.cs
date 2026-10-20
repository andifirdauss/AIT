using AiThread.Common.DTOs;
using AiThread.Service.Interfaces;
using AiThread.Service.Skills;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed partial class SkillService
{
    public async Task<bool> DeleteSourceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var source = await db.SkillSources.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (source is null) return false;

        var attached = await db.NodeSkills.CountAsync(ns => ns.Skill!.SkillSourceId == id, cancellationToken);
        db.SkillSources.Remove(source);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            SkillPaths.DeleteDirectory(paths.SourceDir(id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Folder of skill source {SourceId} could not be deleted", id);
        }

        eventBus.Publish(AppEvent.Create(AppEventTypes.SkillChanged, new { sourceId = id, action = "deleted" }));
        if (attached > 0) eventBus.Publish(AppEvent.Create(AppEventTypes.NodeChanged, new { action = "skills" }));
        activityLog.Info("skill", $"Sumber skill {source.Name} dihapus{(attached > 0 ? $" (dilepas dari {attached} lampiran node)" : string.Empty)}.");
        return true;
    }
}
