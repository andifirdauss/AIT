using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Orchestration;

public sealed partial class ContextKeeper
{
    // In the order of ids; threads of other projects are left out.
    private async Task<List<TurnRecord>> LoadTurnsAsync(Guid projectId, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var threads = await db.Threads.AsNoTracking()
            .Where(t => ids.Contains(t.Id) && t.ProjectId == projectId)
            .Select(t => new { t.Id, t.CreatedAt, t.Status, t.Prompt, t.PlanSummary, t.PlanJson, t.FinalResponse, t.Error, t.TestingJson, t.UnitTestJson, t.InvestigationJson })
            .ToListAsync(cancellationToken);
        var tasks = (await db.ThreadTasks.AsNoTracking()
                .Where(t => ids.Contains(t.ThreadRunId))
                .OrderBy(t => t.Order)
                .Select(t => new { t.ThreadRunId, t.TaskKey, t.FilePath, t.TargetPath, t.Action, t.Title, t.Status, t.Summary, t.Error })
                .ToListAsync(cancellationToken))
            .ToLookup(t => t.ThreadRunId);

        var records = new List<TurnRecord>();
        foreach (var id in ids)
        {
            var t = threads.FirstOrDefault(x => x.Id == id);
            if (t is null) continue;
            records.Add(new TurnRecord(
                t.Id,
                t.CreatedAt,
                t.Status.ToString(),
                t.Prompt,
                t.PlanSummary,
                t.PlanJson is not null && ThreadPlan.TryParse(t.PlanJson, out var plan, out _) && plan is not null ? plan.Contract : [],
                tasks[t.Id].Select(x => new TurnTask(x.TaskKey, x.TargetPath is null ? x.FilePath : $"{x.FilePath} → {x.TargetPath}", x.Action, x.Title, x.Status.ToString(), x.Summary ?? x.Error)).ToList(),
                t.FinalResponse,
                t.Error,
                Testing(TestingReport.FromJson(t.TestingJson)?.Brief(), UnitTestReport.FromJson(t.UnitTestJson)?.Summary),
                InvestigationLog.FromJson(t.InvestigationJson)?.Brief(MaxInvestigationChars)));
        }
        return records;
    }
}
