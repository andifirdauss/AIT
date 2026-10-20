using AIThread.Common.Entities;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    private static int TotalTokens(ThreadRun thread) => thread.PlannerTokens + thread.ToolsNodeTokens + thread.ExecutorTokens + thread.ReviewerTokens + thread.WrapperTokens;
}
