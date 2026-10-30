using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Data.Context;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class ThreadService(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    IThreadRegistry registry,
    IPendingQuestionStore questions,
    IEventBus eventBus,
    ILogger<ThreadService> logger) : IThreadService
{
    // aithread chat sessions are found among this many latest threads of a project; a title keeps this many characters.
    private const int ChatSessionScan = 1_000;
    private const int ChatSessionTitleChars = 80;

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
        t.PlannerTokens + t.ToolsNodeTokens + t.ExecutorTokens + t.ReviewerTokens + t.TesterTokens + t.WrapperTokens,
        t.CreatedAt,
        t.CompletedAt,
        t.GitState);
}
