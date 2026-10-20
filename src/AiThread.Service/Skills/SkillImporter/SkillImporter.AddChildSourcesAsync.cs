using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Skills;

public sealed partial class SkillImporter
{
    // Repositories a web page points to become sources of their own (one level deep: never another web page).
    private async Task<int> AddChildSourcesAsync(SkillSource parent, IReadOnlyList<string> links, CancellationToken cancellationToken)
    {
        if (links.Count == 0) return 0;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var added = new List<SkillSource>();
        var accepted = 0;
        foreach (var link in links.Take(MaxChildSources))
        {
            SkillSourceLocation location;
            try
            {
                location = SkillSourceLocator.Locate(link);
            }
            catch (AppValidationException)
            {
                continue;
            }
            if (location.Kind == SkillSourceKind.Web) continue;

            accepted++;
            if (await db.SkillSources.AnyAsync(s => s.Url == location.Url, cancellationToken) || added.Any(s => s.Url == location.Url)) continue;
            var child = new SkillSource
            {
                Url = location.Url,
                Name = location.Name,
                Kind = location.Kind,
                Ref = location.Ref,
                SubPath = location.SubPath,
                ParentSourceId = parent.Id
            };
            db.SkillSources.Add(child);
            added.Add(child);
        }
        await db.SaveChangesAsync(cancellationToken);

        foreach (var child in added)
        {
            queue.Enqueue(child.Id);
            eventBus.Publish(AppEvent.Create(AppEventTypes.SkillChanged, new { sourceId = child.Id, status = child.Status.ToString(), action = "added" }));
        }
        return accepted;
    }
}
