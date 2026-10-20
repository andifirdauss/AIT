using AiThread.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.CodeCalls;

public sealed partial class FunctionCallStore
{
    // The recorded calls into one function (path relative, symbol as calls resolve to: "Type.Member" or a function name).
    public async Task<IReadOnlyList<FunctionCaller>> CallersAsync(Guid projectId, string path, string symbol, int max, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.CodeFunctionCalls.AsNoTracking()
            .Where(c => c.ResolvedPath == path && c.ResolvedSymbol == symbol
                        && c.CodeFunctionScan!.ProjectId == projectId && c.CodeFunctionScan.Status == FunctionScanStatus.Done)
            .OrderBy(c => c.CodeFunctionScan!.Path)
            .ThenBy(c => c.Line)
            .Select(c => new FunctionCaller(c.CodeFunctionScan!.Path, c.CodeFunctionScan.Symbol, c.Line))
            .Take(max)
            .ToListAsync(cancellationToken);
    }
}
