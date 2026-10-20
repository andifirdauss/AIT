using AIThread.Common.DTOs;
using AIThread.Service.Skills;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class SkillService
{
    public async Task<SkillDetailDto?> GetSkillAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var skill = await db.Skills.AsNoTracking()
            .Include(s => s.SkillSource)
            .Include(s => s.Nodes).ThenInclude(n => n.AiNode)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (skill is null) return null;

        var folder = paths.SkillDir(skill.SkillSourceId, skill.RelativeDir);
        return new SkillDetailDto(
            skill.Id,
            skill.SkillSourceId,
            skill.SkillSource?.Name ?? string.Empty,
            skill.SkillSource?.Url ?? string.Empty,
            skill.Name,
            skill.Description,
            skill.Tags,
            folder,
            skill.EntryFile,
            skill.Content,
            SkillDiscovery.ListFiles(folder, MaxDetailFiles),
            skill.Nodes.Select(n => n.AiNode?.Name).OfType<string>().OrderBy(n => n).ToList());
    }
}
