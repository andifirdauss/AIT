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
        await progress.StepAsync($"🟡 {agent} {From.Model} tidak tersedia ({Reason}) → pindah ke {To.Model}", cancellationToken, hang: 3);
    }
}

public sealed record NodeReply(ChatCompletionResult Result, AiNode Node);

// One model call for an agent, with the node's fallback chain (AiNode.FallbackNodeId): when the node stays unavailable
// after 9router's own retries (429, 408, 5xx), the same call goes to its fallback, then to that one's fallback, and so
// on. A call that already streamed text to the client is never repeated, and a chain is never walked twice.
public sealed partial class NodeCaller(INineRouterService router, INodeConcurrencyLimiter limiter, INodeService nodeService) : INodeCaller
{
}
