using AIThread.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.CodeCalls;

public sealed partial class FunctionCallStore
{
    public async Task<int> CountAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.CodeFunctionScans.CountAsync(s => s.ProjectId == projectId && s.Status == FunctionScanStatus.Done, cancellationToken);
    }
}
