using AiThread.Common.Entities;
using AiThread.Data.Context;
using AiThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class NodeService
{
    // null = leave the node's skills as they are (clients that do not know about skills).
    private static async Task ApplySkillsAsync(AiThreadDbContext db, AiNode node, IReadOnlyList<Guid>? skillIds, CancellationToken cancellationToken)
    {
        if (skillIds is null) return;
        var wanted = skillIds.Distinct().ToList();
        if (wanted.Count > MaxSkillsPerNode)
            throw new AppValidationException($"Satu node maksimal {MaxSkillsPerNode} skill — skill yang terlalu banyak membuat prompt boros dan tidak fokus.");

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
