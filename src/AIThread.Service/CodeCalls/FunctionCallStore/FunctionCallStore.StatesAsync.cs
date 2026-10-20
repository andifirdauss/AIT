using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.CodeCalls;

public sealed partial class FunctionCallStore
{
    public async Task<IReadOnlyDictionary<string, FunctionScanState>> StatesAsync(Guid projectId, IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
    {
        if (keys.Count == 0) return new Dictionary<string, FunctionScanState>();
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var result = new Dictionary<string, FunctionScanState>(StringComparer.Ordinal);
        foreach (var chunk in keys.Distinct().Chunk(500))
        {
            var rows = await db.CodeFunctionScans.AsNoTracking()
                .Where(s => s.ProjectId == projectId && chunk.Contains(s.Key))
                .Select(s => new { s.Key, s.Status, s.BodyHash, Calls = s.Calls.Count, s.UpdatedAt, s.CreatedAt })
                .ToListAsync(cancellationToken);
            foreach (var row in rows) result[row.Key] = new FunctionScanState(row.Status, row.BodyHash, row.Calls, row.UpdatedAt ?? row.CreatedAt);
        }
        return result;
    }
}
