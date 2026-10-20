using AiThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Memory;

public sealed partial class CodeMapper
{
    public async Task<IReadOnlyList<CodeMapEntry>> ListAsync(string key, string? search, string? module, int take, CancellationToken cancellationToken)
    {
        await using var db = await store.CreateCatalogAsync(cancellationToken);
        var query = db.CodeMapEntries.AsNoTracking().Where(e => e.ProjectKey == key);
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(e => e.Path.Contains(search.Trim()));
        if (!string.IsNullOrWhiteSpace(module)) query = query.Where(e => e.Module == module);
        return await query.OrderBy(e => e.Path).Take(take).ToListAsync(cancellationToken);
    }
}
