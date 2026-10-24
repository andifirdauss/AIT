using System.Diagnostics;
using AIThread.Common.Entities;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class NodeCaller
{
    // build creates the request for the node about to be called (the system prompt differs per node).
    // slotHeld: the caller already holds a concurrency slot of `node` (not of its fallbacks).
    public async Task<NodeReply> ChatAsync(
        AiNode node,
        Func<AiNode, Task<ChatCompletionRequest>> build,
        CancellationToken cancellationToken,
        Func<string, Task>? onDelta = null,
        Func<NodeSwitch, Task>? onSwitch = null,
        bool slotHeld = false)
    {
        var visited = new HashSet<Guid>();
        var current = node;
        var streamed = false;
        var clock = Stopwatch.StartNew();
        var queued = TimeSpan.Zero;
        Func<string, Task>? relay = onDelta is null ? null : delta =>
        {
            streamed = true;
            return onDelta(delta);
        };

        while (true)
        {
            visited.Add(current.Id);
            try
            {
                var request = await build(current);
                if (slotHeld && current.Id == node.Id) return Reply(await router.ChatAsync(request, relay, cancellationToken));
                var waiting = Stopwatch.StartNew();
                using (await limiter.AcquireAsync(current, cancellationToken))
                {
                    queued += waiting.Elapsed;
                    return Reply(await router.ChatAsync(request, relay, cancellationToken));
                }
            }
            catch (NineRouterException ex) when (ex.IsModelUnavailable && !streamed && !cancellationToken.IsCancellationRequested
                                                 && current.FallbackNodeId is { } nextId && !visited.Contains(nextId))
            {
                var next = await nodeService.GetNodeAsync(nextId, cancellationToken);
                if (next is null || next.Role != current.Role) throw;
                if (onSwitch is not null) await onSwitch(new NodeSwitch(current, next, ex));
                current = next;
            }
        }

        NodeReply Reply(ChatCompletionResult result) => new(result, current, clock.Elapsed - queued, queued);
    }
}
