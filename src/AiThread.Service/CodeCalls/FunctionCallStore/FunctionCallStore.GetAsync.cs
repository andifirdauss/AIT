using AiThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.CodeCalls;

public sealed partial class FunctionCallStore
{
    public async Task<CodeFunctionScan?> GetAsync(Guid projectId, string key, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.CodeFunctionScans.AsNoTracking()
            .Include(s => s.Calls.OrderBy(c => c.Line))
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Key == key, cancellationToken);
    }
}
