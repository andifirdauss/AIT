using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Skills;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class SkillService
{
    public async Task<SkillSourceDto> AddSourceAsync(AddSkillSourceRequest request, CancellationToken cancellationToken = default)
    {
        var location = SkillSourceLocator.Locate(request.Url);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.SkillSources.AnyAsync(s => s.Url == location.Url, cancellationToken))
            throw new AppValidationException("Link ini sudah ada di gudang skill. Klik Sync untuk menarik ulang.");

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
        activityLog.Info("skill", $"Sumber skill {source.Name} ditambahkan dan masuk antrean.");
        return ToDto(source);
    }
}
