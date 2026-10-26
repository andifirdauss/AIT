using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
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
        if (planners == 0) issues.Add(Lang.T("Belum ada node Planner aktif.", "No active Planner node yet."));
        if (executors == 0) issues.Add(Lang.T("Belum ada node Executor aktif.", "No active Executor node yet."));
        if (wrappers == 0) issues.Add(Lang.T("Belum ada node Wrapper aktif.", "No active Wrapper node yet."));
        if (wrappers > 1) issues.Add(Lang.T("Lebih dari satu Wrapper aktif — yang dipakai adalah prioritas terkecil.", "More than one active Wrapper — the one with the lowest priority is used."));
        if (Count(NodeRole.Reviewer) > 1) issues.Add(Lang.T("Lebih dari satu Reviewer aktif — yang dipakai adalah prioritas terkecil.", "More than one active Reviewer — the one with the lowest priority is used."));
        if (Count(NodeRole.Tester) > 1) issues.Add(Lang.T("Lebih dari satu Tester aktif — yang dipakai adalah prioritas terkecil.", "More than one active Tester — the one with the lowest priority is used."));
        if (Count(NodeRole.Tester) == 0 && await db.Nodes.AnyAsync(n => n.IsActive && n.Role == NodeRole.Planner && n.AutoTesting, cancellationToken))
            issues.Add(Lang.T("Auto Testing menyala di planner, tetapi belum ada node Tester aktif — testing dilewati.", "Auto Testing is on in the planner, but there is no active Tester node — testing is skipped."));
        if (Count(NodeRole.Tester) == 0 && await HasUnitTestsAsync(cancellationToken))
            issues.Add(Lang.T("Ada katalog unit test, tetapi belum ada node Tester aktif — unit test yang gagal diperbaiki di kode tanpa triase Tester.", "There is a unit test catalog, but no active Tester node — failing unit tests are fixed in the code without Tester triage."));
        if (await db.PlannerToolDelegations.AnyAsync(d => d.PlannerNode!.IsActive && !d.ToolsNode!.IsActive, cancellationToken))
            issues.Add(Lang.T("Ada tool planner yang didelegasikan ke tools node nonaktif — tool itu dijalankan planner sendiri.", "A planner tool is delegated to an inactive tools node — the planner runs that tool itself."));

        return new PipelineStatusDto(planners > 0 && executors > 0 && wrappers > 0, planners, executors, wrappers, issues);
    }
}
