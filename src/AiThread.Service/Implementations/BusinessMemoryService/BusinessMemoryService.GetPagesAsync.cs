using AiThread.Common.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    public async Task<IReadOnlyList<BusinessPageSummaryDto>> GetPagesAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await ProjectAsync(projectId, cancellationToken);
        if (project is null || string.IsNullOrEmpty(project.ProjectKey)) return [];

        await using var db = await store.CreateAsync(cancellationToken);
        return (await SummaryQuery(db, project.ProjectKey).ToListAsync(cancellationToken)).Select(ToSummary).ToList();
    }
}
