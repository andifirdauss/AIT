using AiThread.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.CodeCalls;

public sealed partial class FunctionCallStore
{
    // Replaces the scan of each function (by key) with the new one, calls included.
    public async Task SaveAsync(Guid projectId, IReadOnlyList<CodeFunctionScan> scans, CancellationToken cancellationToken)
    {
        if (scans.Count == 0) return;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var keys = scans.Select(s => s.Key).ToList();
        var existing = await db.CodeFunctionScans.Include(s => s.Calls)
            .Where(s => s.ProjectId == projectId && keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var scan in scans)
        {
            if (existing.TryGetValue(scan.Key, out var row))
            {
                db.CodeFunctionCalls.RemoveRange(row.Calls);
                row.Path = scan.Path;
                row.Symbol = scan.Symbol;
                row.Signature = scan.Signature;
                row.StartLine = scan.StartLine;
                row.EndLine = scan.EndLine;
                row.BodyHash = scan.BodyHash;
                row.Status = scan.Status;
                row.Error = scan.Error;
                row.Model = scan.Model;
                row.Tokens = scan.Tokens;
                row.UpdatedAt = now;
                foreach (var call in scan.Calls)
                {
                    call.CodeFunctionScanId = row.Id;
                    db.CodeFunctionCalls.Add(call);
                }
            }
            else
            {
                scan.ProjectId = projectId;
                scan.UpdatedAt = now;
                db.CodeFunctionScans.Add(scan);
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
