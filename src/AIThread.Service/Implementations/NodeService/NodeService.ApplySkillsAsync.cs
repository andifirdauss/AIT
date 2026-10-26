using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Data.Context;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class NodeService
{
    // null = leave the node's skills as they are (clients that do not know about skills).
    private static async Task ApplySkillsAsync(AIThreadDbContext db, AiNode node, IReadOnlyList<Guid>? skillIds, CancellationToken cancellationToken)
    {
        if (skillIds is null) return;
        var wanted = skillIds.Distinct().ToList();
        if (wanted.Count > MaxSkillsPerNode)
            throw new AppValidationException(Lang.T($"Satu node maksimal {MaxSkillsPerNode} skill — skill yang terlalu banyak membuat prompt boros dan tidak fokus.", $"One node has at most {MaxSkillsPerNode} skills — too many skills make the prompt wasteful and unfocused."));

        var skills = await db.Skills.Include(s => s.SkillSource).Where(s => wanted.Contains(s.Id)).ToListAsync(cancellationToken);
        node.Skills.RemoveAll(ns => !wanted.Contains(ns.SkillId));
        for (var order = 0; order < wanted.Count; order++)
        {
            var skill = skills.FirstOrDefault(s => s.Id == wanted[order]);
            if (skill is null) continue; // removed from the store meanwhile
            var existing = node.Skills.FirstOrDefault(ns => ns.SkillId == skill.Id);
            if (existing is not null) existing.Order = order;
            else node.Skills.Add(new NodeSkill { AiNodeId = node.Id, SkillId = skill.Id, Skill = skill, Order = order });
        }
    }
}
