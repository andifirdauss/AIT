using AiThread.Common.DTOs;
using AiThread.Service.Business;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    public async Task<IReadOnlyList<(BusinessPageSummaryDto Page, string Content)>> GetForPlannerAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().Take(20).ToList();
        if (idList.Count == 0) return [];

        await using var db = await store.CreateAsync(cancellationToken);
        var rows = await db.BusinessPages.AsNoTracking().Where(p => idList.Contains(p.Id)).ToListAsync(cancellationToken);
        return rows.Select(p =>
        {
            var content = BusinessMarkdown.ForPlanner(p.Content);
            if (content.Length > PlannerContentMaxChars) content = content[..PlannerContentMaxChars] + "\n… (page truncated)";
            return (ToSummary(p), content);
        }).ToList();
    }
}
