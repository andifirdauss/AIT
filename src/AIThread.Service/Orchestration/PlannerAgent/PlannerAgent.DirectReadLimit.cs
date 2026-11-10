using AIThread.Common.Entities;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // Null at 100%: the planner does all its reading itself.
    private static long? DirectReadLimit(AiNode node) =>
        node.DirectReadPercent >= 100 ? null : DirectReadBudgetChars * node.DirectReadPercent / 100;
}
