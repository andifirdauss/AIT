using AIThread.Common.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class ThreadService
{
    public async Task<ThreadDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var summary = await db.Threads.AsNoTracking().Where(t => t.Id == id).Select(SummaryProjection).FirstOrDefaultAsync(cancellationToken);
        if (summary is null) return null;

        var thread = await db.Threads.AsNoTracking().FirstAsync(t => t.Id == id, cancellationToken);
        var tasks = await db.ThreadTasks.AsNoTracking()
            .Where(t => t.ThreadRunId == id)
            .OrderBy(t => t.Order)
            .ToListAsync(cancellationToken);

        return new ThreadDetailDto(
            summary,
            thread.PlanJson,
            thread.FinalResponse,
            thread.Error,
            thread.PlannerModel,
            thread.WrapperModel,
            thread.PlannerSteps,
            thread.PlannerTokens,
            thread.ExecutorTokens,
            thread.WrapperTokens,
            thread.PlanningMs,
            thread.WrappingMs,
            thread.WorkingDirectory,
            tasks.Select(ToTaskDto).ToList(),
            thread.ContextPack,
            thread.ReviewerModel,
            thread.ReviewerTokens,
            thread.VerificationMs,
            thread.VerificationJson,
            thread.PlannerCachedTokens,
            thread.RetryCount,
            thread.RolledBackAt,
            thread.ToolsNodeTokens,
            thread.TesterModel,
            thread.TesterTokens,
            thread.TestingMs,
            thread.TestingJson,
            thread.UnitTestMs,
            thread.UnitTestJson);
    }
}
