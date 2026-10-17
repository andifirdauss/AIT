using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.CodeCalls;

public sealed record FunctionScanState(FunctionScanStatus Status, string BodyHash, int Calls, DateTime? UpdatedAt);

// A function that calls another one, at Line of Path.
public sealed record FunctionCaller(string Path, string Symbol, int Line);

// The scanned functions of a project (CodeFunctionScans) and their calls, in the main database of this machine.
public sealed class FunctionCallStore(IDbContextFactory<AiThreadDbContext> dbFactory)
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

    public async Task<CodeFunctionScan?> GetAsync(Guid projectId, string key, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.CodeFunctionScans.AsNoTracking()
            .Include(s => s.Calls.OrderBy(c => c.Line))
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Key == key, cancellationToken);
    }

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

    public async Task<int> CountAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.CodeFunctionScans.CountAsync(s => s.ProjectId == projectId && s.Status == FunctionScanStatus.Done, cancellationToken);
    }

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
