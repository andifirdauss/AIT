using AIThread.Service.Interfaces;

namespace AIThread.Service.Orchestration;

// Runs the read-only investigation tools (git included), run_build and run_tests, for the planner and for the tools
// nodes it delegates tools to.
// prefix marks the progress lines of a tools node ("#1 › "); empty for the planner itself.
public sealed partial class PlannerToolRunner(
    IMemoryService memoryService,
    IContextKeeper contextKeeper,
    IBuildRunner buildRunner,
    ICodeMapper codeMapper,
    IFunctionCallQueue functionCalls,
    ICodeSkeletonTools skeleton,
    IUnitTestStage unitTests,
    ISecretGuard secrets) : IPlannerToolRunner
{
    // Paths and queries are shortened in the client progress so a tool line rarely wraps; the activity log keeps them whole.
    private const int ProgressPathChars = 48;
    private const int ProgressQueryChars = 60;

    // Tool name plus its prefix ("#1 › ") padded to one column, so badges and arguments line up with or without a prefix.
    public static int LeadColumn => ClientProgress.DisplayWidth("#9 › ") + PlannerTools.NameColumn;
}
