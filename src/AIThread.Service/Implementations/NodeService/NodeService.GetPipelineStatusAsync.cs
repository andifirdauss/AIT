using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class NodeService
{
    public async Task<PipelineStatusDto> GetPipelineStatusAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var counts = await db.Nodes.AsNoTracking()
            .Where(n => n.IsActive)
            .GroupBy(n => n.Role)
            .Select(g => new { Role = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int Count(NodeRole role) => counts.FirstOrDefault(c => c.Role == role)?.Count ?? 0;
        var planners = Count(NodeRole.Planner);
        var executors = Count(NodeRole.Executor);
        var wrappers = Count(NodeRole.Wrapper);

        var issues = new List<string>();
        if (planners == 0) issues.Add("Belum ada node Planner aktif.");
        if (executors == 0) issues.Add("Belum ada node Executor aktif.");
        if (wrappers == 0) issues.Add("Belum ada node Wrapper aktif.");
        if (wrappers > 1) issues.Add("Lebih dari satu Wrapper aktif — yang dipakai adalah prioritas terkecil.");
        if (Count(NodeRole.Reviewer) > 1) issues.Add("Lebih dari satu Reviewer aktif — yang dipakai adalah prioritas terkecil.");
        if (Count(NodeRole.Tester) > 1) issues.Add("Lebih dari satu Tester aktif — yang dipakai adalah prioritas terkecil.");
        if (Count(NodeRole.Tester) == 0 && await db.Nodes.AnyAsync(n => n.IsActive && n.Role == NodeRole.Planner && n.AutoTesting, cancellationToken))
            issues.Add("Auto Testing menyala di planner, tetapi belum ada node Tester aktif — testing dilewati.");
        if (Count(NodeRole.Tester) == 0 && await HasUnitTestsAsync(cancellationToken))
            issues.Add("Ada katalog unit test, tetapi belum ada node Tester aktif — unit test yang gagal diperbaiki di kode tanpa triase Tester.");
        if (await db.PlannerToolDelegations.AnyAsync(d => d.PlannerNode!.IsActive && !d.ToolsNode!.IsActive, cancellationToken))
            issues.Add("Ada tool planner yang didelegasikan ke tools node nonaktif — tool itu dijalankan planner sendiri.");

        return new PipelineStatusDto(planners > 0 && executors > 0 && wrappers > 0, planners, executors, wrappers, issues);
    }
}
