using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.Skills;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class SkillService
{
    public async Task<SkillSourceDto> AddSourceAsync(AddSkillSourceRequest request, CancellationToken cancellationToken = default)
    {
        var location = SkillSourceLocator.Locate(request.Url);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.SkillSources.AnyAsync(s => s.Url == location.Url, cancellationToken))
            throw new AppValidationException(Lang.T("Link ini sudah ada di gudang skill. Klik Sync untuk menarik ulang.", "This link is already in the skill store. Click Sync to fetch it again."));

        var source = new SkillSource
        {
            Url = location.Url,
            Name = location.Name,
            Kind = location.Kind,
            Ref = location.Ref,
            SubPath = location.SubPath
        };
        db.SkillSources.Add(source);
        await db.SaveChangesAsync(cancellationToken);

        queue.Enqueue(source.Id);
        eventBus.Publish(AppEvent.Create(AppEventTypes.SkillChanged, new { sourceId = source.Id, status = source.Status.ToString(), action = "added" }));
        activityLog.Info("skill", Lang.T($"Sumber skill {source.Name} ditambahkan dan masuk antrean.", $"Skill source {source.Name} added and queued."));
        return ToDto(source);
    }
}
