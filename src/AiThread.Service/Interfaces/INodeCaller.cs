using AiThread.Common.Entities;
using AiThread.Service.Models;
using AiThread.Service.Orchestration;

namespace AiThread.Service.Interfaces;

public interface INodeCaller
{
    Task<NodeReply> ChatAsync(AiNode node, Func<AiNode, Task<ChatCompletionRequest>> build, CancellationToken cancellationToken, Func<string, Task>? onDelta = null, Func<NodeSwitch, Task>? onSwitch = null, bool slotHeld = false);
}
