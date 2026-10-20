using AiThread.Common.Entities;

namespace AiThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    private static PlanningResult Finished(ThreadPlan plan, PlannerRun run, AiNode node) =>
        new(plan, run.Step, run.Tokens, run.CachedTokens, node, MemoriesUsed: run.MemoriesRead.Count, ToolsNodeTokens: run.ToolsNodeTokens);
}
