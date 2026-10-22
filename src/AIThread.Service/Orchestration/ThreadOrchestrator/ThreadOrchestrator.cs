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
// reviewer, with fix rounds) -> testing (Auto Testing: the tester uses the running app) -> wrapper (pools and reports).
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
    IUnitTestStage unitTestStage,
    ITesterAgent testerAgent,
    IWrapperAgent wrapperAgent,
    IMemoryExtractor memoryExtractor,
    ICodeMapper codeMapper,
    IContextKeeper contextKeeper,
    ICodeStructureService codeStructure,
    IFunctionCallQueue functionCalls,
    IPendingQuestionStore questions,
    ITestLoginStore testLogins,
    ITestAccountStore testAccounts,
    ISecretGuard secrets,
    INineRouterService router,
    IHostApplicationLifetime lifetime,
    ILogger<ThreadOrchestrator> logger) : IThreadOrchestrator
{
    // How long the final answer waits for the memory extraction before its footer says "still running".
    private static readonly TimeSpan MemoryWait = TimeSpan.FromSeconds(30);

    // Files of the session a follow-up's labels are taken from, at most.
    private const int MaxSessionLabelFiles = 30;

    // What the planner of the session's last thread went through, as far as it goes into the next brief.
    private const int MaxPreviousInvestigationChars = 8_000;

    private sealed record SessionThread(Guid Id, string Prompt, IReadOnlyList<string> Files, InvestigationLog? Investigation);

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

    // A finished thread whose test runs again: the stored plan with every succeeded task, and their results.
    private sealed record PreparedRetest(
        ThreadRun Thread,
        ProjectContext Project,
        ThreadPlan Plan,
        ExecutionSummary Execution,
        AiNode Tester,
        CancellationTokenSource Cancellation);

    // What verification and wrapping of a run need. MemoryNode extracts memories afterwards (null = skip);
    // KeepContext updates the Claude Code context pack (not for retries, which no chat turn started); MemoriesUsed is
    // how many memories the planner read, for the footer. Tester is set when the planner has Auto Testing on.
    // UnitTests: the project's unit test catalog for this thread (null: none run); Triage: the Tester node that triages
    // failing unit tests, whether Auto Testing is on or not.
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
        int MemoriesUsed = 0,
        AiNode? Tester = null,
        UnitTestSession? UnitTests = null,
        AiNode? Triage = null);
}
