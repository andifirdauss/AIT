using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;

namespace AiThread.Service.Orchestration;

public sealed record NodeSwitch(AiNode From, AiNode To, NineRouterException Error)
{
    public string Reason => Error.StatusCode switch
    {
        429 => "rate limit 429",
        408 => "timeout",
        { } code => $"HTTP {code}",
        null => "tidak dapat dihubungi"
    };

    // agent: how the developer knows the role ("Planner", "Executor T2", ...).
    public async Task ReportAsync(string agent, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        recorder.Log(ActivityLevel.Warning, $"{agent} `{From.Model}` gagal ({Error.Message}); dialihkan ke node cadangan '{To.Name}' (`{To.Model}`).");
        await progress.LineAsync($"   ⚠️ {agent} `{From.Model}` tidak tersedia ({Reason}), beralih ke `{To.Model}`…", cancellationToken);
    }
}

public sealed record NodeReply(ChatCompletionResult Result, AiNode Node);

// One model call for an agent, with the node's fallback chain (AiNode.FallbackNodeId): when the node stays unavailable
// after 9router's own retries (429, 408, 5xx), the same call goes to its fallback, then to that one's fallback, and so
// on. A call that already streamed text to the client is never repeated, and a chain is never walked twice.
public sealed class NodeCaller(INineRouterService router, NodeConcurrencyLimiter limiter, INodeService nodeService)
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
                if (slotHeld && current.Id == node.Id) return new NodeReply(await router.ChatAsync(request, relay, cancellationToken), current);
                using (await limiter.AcquireAsync(current, cancellationToken))
                    return new NodeReply(await router.ChatAsync(request, relay, cancellationToken), current);
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
    }
}
