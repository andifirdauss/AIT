using AIThread.Common.Entities;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    private static PlanningResult Finished(ThreadPlan plan, PlannerRun run, AiNode node) =>
        new(plan, run.Step, run.Tokens, run.CachedTokens, node, MemoriesUsed: run.MemoriesRead.Count, ToolsNodeTokens: run.ToolsNodeTokens,
            ModelTime: run.ModelTime, ToolTime: run.ToolTime, QueueTime: run.QueueTime);
}
