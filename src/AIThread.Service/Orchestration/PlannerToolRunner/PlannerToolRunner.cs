using AIThread.Service.Interfaces;

namespace AIThread.Service.Orchestration;

// Runs the read-only investigation tools (git included), run_build, run_tests, run_command and the background
// processes (start_process, process_output, stop_process), for the planner and for the tools nodes it delegates tools to.
// prefix marks the progress lines of a tools node ("#1 › "); empty for the planner itself.
public sealed partial class PlannerToolRunner(
    IMemoryService memoryService,
    IContextKeeper contextKeeper,
    IBuildRunner buildRunner,
    ICodeMapper codeMapper,
    IFunctionCallQueue functionCalls,
    ICodeSkeletonTools skeleton,
    IUnitTestStage unitTests,
    ISecretGuard secrets,
    ICommandRunner commandRunner,
    IApprovalService approvals,
    IBackgroundProcesses processes,
    IWebFetcher webFetcher) : IPlannerToolRunner
{
    // Paths and queries are shortened in the client progress so a tool line rarely wraps; the activity log keeps them whole.
    private const int ProgressPathChars = 48;
    private const int ProgressQueryChars = 60;

    // process_output returns this many last lines unless asked (at most MaxTailLines); stop_process StopTailLines.
    private const int DefaultTailLines = 100;
    private const int MaxTailLines = 500;
    private const int StopTailLines = 30;

    // The most of a web page (as text) one web_fetch hands the model; max_chars may ask for less.
    public const int MaxFetchChars = 24_000;

    // Tool name plus its prefix ("#1 › ") padded to one column, so badges and arguments line up with or without a prefix.
    public static int LeadColumn => ClientProgress.DisplayWidth("#9 › ") + PlannerTools.NameColumn;
}
