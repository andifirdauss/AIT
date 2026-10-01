using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Context;
using AiThread.Service.Interfaces;
using AiThread.Service.Orchestration;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed class ThreadService(IDbContextFactory<AiThreadDbContext> dbFactory, ThreadRegistry registry) : IThreadService
{
    public async Task<IReadOnlyList<ThreadSummaryDto>> GetRecentAsync(int take, Guid? projectId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Threads.AsNoTracking();
        if (projectId is { } id) query = query.Where(t => t.ProjectId == id);

        return await query
            .OrderByDescending(t => t.CreatedAt)
            .Take(Math.Clamp(take, 1, 200))
            .Select(SummaryProjection)
            .ToListAsync(cancellationToken);
    }

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
            thread.VerificationJson);
    }

    public bool Cancel(Guid id) => registry.Cancel(id);

    public static ThreadTaskDto ToTaskDto(ThreadTask t) => new(
        t.Id, t.TaskKey, t.Order, t.Title, t.FilePath, t.Action, t.Instruction, t.DependsOn, t.Status,
        t.NodeName, t.Model, t.Attempts, t.Summary, t.Diff, t.Error, t.Tokens, t.StartedAt, t.CompletedAt, t.DurationMs);

    private static readonly System.Linq.Expressions.Expression<Func<ThreadRun, ThreadSummaryDto>> SummaryProjection = t => new ThreadSummaryDto(
        t.Id,
        t.ProjectId,
        t.Project != null ? t.Project.Name : null,
        t.Source,
        t.SessionId,
        t.Prompt.Length > 400 ? t.Prompt.Substring(0, 400) : t.Prompt,
        t.Status,
        t.PlanSummary,
        t.Tasks.Count(),
        t.Tasks.Count(x => x.Status == ThreadTaskStatus.Succeeded),
        t.Tasks.Count(x => x.Status == ThreadTaskStatus.Failed),
        t.TotalMs,
        t.ExecutionWallMs,
        t.ExecutionSumMs,
        t.PlannerTokens + t.ExecutorTokens + t.ReviewerTokens + t.WrapperTokens,
        t.CreatedAt,
        t.CompletedAt);
}
