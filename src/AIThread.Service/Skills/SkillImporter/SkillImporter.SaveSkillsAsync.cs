using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Skills;

public sealed partial class SkillImporter
{
    private async Task<(int Added, int Updated, int Removed)> SaveSkillsAsync(Guid sourceId, IReadOnlyList<DiscoveredSkill> skills, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var source = await db.SkillSources.Include(s => s.Skills).FirstOrDefaultAsync(s => s.Id == sourceId, cancellationToken);
        if (source is null) return (0, 0, 0);

        static string Key(string dir, string file) => $"{dir}|{file}".ToLowerInvariant();
        var existing = source.Skills.ToDictionary(s => Key(s.RelativeDir, s.EntryFile));
        var seen = new HashSet<string>();
        int added = 0, updated = 0;

        foreach (var found in skills)
        {
            var key = Key(found.RelativeDir, found.EntryFile);
            if (!seen.Add(key)) continue;
            var hash = SkillDiscovery.Hash(found.Body);
            // A pull without the orchestrator (model down) keeps the tags of the previous pull.
            var tags = found.Tags ?? (existing.TryGetValue(key, out var previous) ? previous.Tags : null);
            if (existing.TryGetValue(key, out var skill))
            {
                if (skill.ContentHash != hash || skill.Name != found.Name || skill.Description != found.Description || skill.Tags != tags)
                {
                    updated++;
                    skill.UpdatedAt = DateTime.UtcNow;
                }
            }
            else
            {
                // Added explicitly: Skill gets its Guid key up front, and a keyed entity found only through a navigation
                // is taken for an existing row (UPDATE of 0 rows).
                skill = new Skill { SkillSourceId = sourceId, RelativeDir = found.RelativeDir, EntryFile = found.EntryFile };
                db.Skills.Add(skill);
                added++;
            }
            skill.Name = found.Name;
            skill.Description = found.Description;
            skill.Tags = tags;
            skill.Content = found.Body;
            skill.ContentHash = hash;
        }

        var removed = source.Skills.Where(s => existing.ContainsValue(s) && !seen.Contains(Key(s.RelativeDir, s.EntryFile))).ToList();
        foreach (var skill in removed)
        {
            // Cascades to NodeSkills: the skill disappears from every node it was attached to.
            source.Skills.Remove(skill);
            db.Skills.Remove(skill);
        }
        if (removed.Count > 0)
            activityLog.Warn("skill", Lang.T($"{removed.Count} skill tidak ada lagi di {source.Name} dan dihapus dari gudang: {string.Join(", ", removed.Select(s => s.Name).Take(10))}.", $"{removed.Count} skills no longer exist in {source.Name} and were removed from the store: {string.Join(", ", removed.Select(s => s.Name).Take(10))}."));

        source.Status = SkillSourceStatus.Ready;
        source.Error = null;
        source.LastPulledAt = DateTime.UtcNow;
        source.SkillCount = seen.Count;
        source.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        eventBus.Publish(AppEvent.Create(AppEventTypes.SkillChanged, new { sourceId, status = source.Status.ToString() }));
        if (removed.Count > 0) eventBus.Publish(AppEvent.Create(AppEventTypes.NodeChanged, new { action = "skills" }));
        return (added, updated, removed.Count);
    }
}
