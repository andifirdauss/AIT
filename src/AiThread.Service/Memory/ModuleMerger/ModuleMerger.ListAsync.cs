using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Memory;

public sealed partial class ModuleMerger
{
    public async Task<IReadOnlyList<ModuleMergeDto>> ListAsync(string key, ModuleMergeStatus? status, int take, CancellationToken cancellationToken)
    {
        await using var catalog = await longStore.CreateCatalogAsync(cancellationToken);
        var query = catalog.ModuleMergeLogs.AsNoTracking().Where(l => l.ProjectKey == key);
        if (status is { } s) query = query.Where(l => l.Status == s);
        var rows = await query.OrderByDescending(l => l.CreatedAt).Take(take).ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }
}
