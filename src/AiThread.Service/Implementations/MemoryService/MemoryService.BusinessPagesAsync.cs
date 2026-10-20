using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Service.Business;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryService
{
    // Business wiki pages as the planner reads them: the label names the page, the description is the page itself
    // with images replaced by their captions.
    private async Task<IReadOnlyList<MemoryItemDto>> BusinessPagesAsync(IReadOnlyList<Guid> ids, LocalProjects projects, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await businessStore.CreateAsync(cancellationToken);
            var pages = await db.BusinessPages.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
            return pages.Select(p =>
            {
                var content = BusinessMarkdown.ForPlanner(p.Content);
                if (content.Length > PlannerPageMaxChars) content = content[..PlannerPageMaxChars] + "\n… (page truncated)";
                var (projectId, projectName) = Owner(projects, null, p.ProjectKey, p.ProjectName);
                return new MemoryItemDto(p.Id, MemoryLevel.Business, MemoryType.Business, projectId, projectName,
                    $"{p.Module} — {p.Title}", content, null, p.CreatedAt, p.UpdatedAt, 0, p.ProjectKey, p.Module);
            }).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read business wiki pages from {Store}.", businessStore.Description);
            return [];
        }
    }
}
