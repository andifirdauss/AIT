using System.Diagnostics;
using AIThread.Common.Entities;
using AIThread.Data.Context;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

// planner (expensive, explores + plans) -> executors (cheap, parallel, one file each) -> verification (build and/or
// reviewer, with fix rounds) -> wrapper (pools and reports).
public sealed partial class ThreadOrchestrator(
    ISettingService settingService,
    INodeService nodeService,
    IProjectService projectService,
    IDbContextFactory<AIThreadDbContext> dbFactory,
    IEventBus eventBus,
    IActivityLogService activityLog,
    IThreadRegistry registry,
    IPlannerAgent plannerAgent,
    IExecutionEngine executionEngine,
    IVerificationStage verificationStage,
    IWrapperAgent wrapperAgent,
    IMemoryExtractor memoryExtractor,
    ICodeMapper codeMapper,
    IContextKeeper contextKeeper,
    ICodeStructureService codeStructure,
    IFunctionCallQueue functionCalls,
    IPendingQuestionStore questions,
    INineRouterService router,
    IHostApplicationLifetime lifetime,
    ILogger<ThreadOrchestrator> logger) : IThreadOrchestrator
{
    // How long the final answer waits for the memory extraction before its footer says "still running".
    private static readonly TimeSpan MemoryWait = TimeSpan.FromSeconds(30);

    // Files of the session a follow-up's labels are taken from, at most.
    private const int MaxSessionLabelFiles = 30;

    private sealed record SessionThread(Guid Id, string Prompt, IReadOnlyList<string> Files);

    // Resume and Reply are set when the thread continues after a planner question; Request then carries the
    // original request plus the clarification.
    private sealed record PreparedThread(ThreadRun Thread, ProjectContext Project, ThreadRequest Request, PendingQuestion? Resume = null, string? Reply = null);

    // Rows are all task rows of the thread; RetryTasks the planned tasks that did not succeed. Cancellation is the
    // registry entry taken while preparing.
    private sealed record PreparedRetry(
        ThreadRun Thread,
        ProjectContext Project,
        ThreadPlan Plan,
        IReadOnlyList<ThreadTask> Rows,
        IReadOnlyList<PlanTask> RetryTasks,
        CancellationTokenSource Cancellation);

    // What verification and wrapping of a run need. MemoryNode extracts memories afterwards (null = skip);
    // KeepContext updates the Claude Code context pack (not for retries, which no chat turn started); MemoriesUsed is
    // how many memories the planner read, for the footer.
    private sealed record Pipeline(
        ThreadRecorder Recorder,
        ClientProgress Progress,
        ThreadRequest Request,
        ProjectContext Project,
        IReadOnlyList<AiNode> Executors,
        AiNode? Wrapper,
        AiNode? MemoryNode,
        WorkspaceScope Scope,
        RuntimeSettings Settings,
        FileJournal Journal,
        Stopwatch Total,
        bool KeepContext,
        int MemoriesUsed = 0);
}
