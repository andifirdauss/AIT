using AiThread.Common.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class SkillService
{
    public async Task<IReadOnlyList<SkillDto>> GetSkillsAsync(string? query, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var skills = db.Skills.AsNoTracking();
        var terms = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var term in terms)
        {
            skills = skills.Where(s => s.Name.Contains(term) || s.Description.Contains(term)
                                      || (s.Tags != null && s.Tags.Contains(term)) || s.SkillSource!.Name.Contains(term)
                                      || s.Content.Contains(term));
        }

        var ordered = skills.OrderBy(s => s.SkillSource!.Name).ThenBy(s => s.Name);
        if (terms.Length == 0)
        {
            return await ordered
                .Select(s => new SkillDto(s.Id, s.SkillSourceId, s.SkillSource!.Name, s.Name, s.Description, s.Tags, s.RelativeDir, s.Nodes.Count))
                .ToListAsync(cancellationToken);
        }

        // With a query, skills matched by name/description/tags come first; a content-only match carries a snippet.
        var rows = await ordered
            .Select(s => new { Dto = new SkillDto(s.Id, s.SkillSourceId, s.SkillSource!.Name, s.Name, s.Description, s.Tags, s.RelativeDir, s.Nodes.Count), s.Content })
            .ToListAsync(cancellationToken);
        return rows
            .Select(r => new { r.Dto, Rank = Rank(r.Dto, terms), Snippet = ContentSnippet(r.Content, terms) })
            .OrderBy(r => r.Rank)
            .Select(r => r.Dto with { Snippet = r.Rank > 1 ? r.Snippet : null })
            .ToList();
    }
}
