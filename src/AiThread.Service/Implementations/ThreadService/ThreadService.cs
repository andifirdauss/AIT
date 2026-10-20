using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Context;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed partial class ThreadService(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    IThreadRegistry registry,
    IPendingQuestionStore questions,
    IEventBus eventBus,
    ILogger<ThreadService> logger) : IThreadService
{
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
        t.PlannerTokens + t.ToolsNodeTokens + t.ExecutorTokens + t.ReviewerTokens + t.WrapperTokens,
        t.CreatedAt,
        t.CompletedAt);
}
