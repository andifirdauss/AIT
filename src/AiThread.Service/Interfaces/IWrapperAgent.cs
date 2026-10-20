using AiThread.Common.Entities;
using AiThread.Service.Orchestration;

namespace AiThread.Service.Interfaces;

public interface IWrapperAgent
{
    Task<(string Text, int Tokens, AiNode Node)> WrapAsync(AiNode node, string userRequest, ThreadPlan plan, ExecutionSummary execution, string? verification, Func<string, Task> onDelta, CancellationToken cancellationToken, Func<NodeSwitch, Task>? onSwitch = null);
}
