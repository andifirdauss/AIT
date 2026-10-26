using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed record NodeSwitch(AiNode From, AiNode To, NineRouterException Error)
{
    public string Reason => Error.StatusCode switch
    {
        429 => "rate limit 429",
        408 => "timeout",
        { } code => $"HTTP {code}",
        null => Lang.T("tidak dapat dihubungi", "cannot be reached")
    };

    // agent: how the developer knows the role ("Planner", "Executor T2", ...).
    public async Task ReportAsync(string agent, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        recorder.Log(ActivityLevel.Warning, Lang.T($"{agent} `{From.Model}` gagal ({Error.Message}); dialihkan ke node cadangan '{To.Name}' (`{To.Model}`).", $"{agent} `{From.Model}` failed ({Error.Message}); switched to the fallback node '{To.Name}' (`{To.Model}`)."));
        await progress.StepAsync(Lang.T($"🟡 {agent} {From.Model} tidak tersedia ({Reason}) → pindah ke {To.Model}", $"🟡 {agent} {From.Model} not available ({Reason}) → switching to {To.Model}"), cancellationToken, hang: 3);
    }
}

// Elapsed: the model's answer time; Queued: the wait for a MaxConcurrency slot. Both cover the whole fallback chain.
public sealed record NodeReply(ChatCompletionResult Result, AiNode Node, TimeSpan Elapsed = default, TimeSpan Queued = default);

// One model call for an agent, with the node's fallback chain (AiNode.FallbackNodeId): when the node stays unavailable
// after 9router's own retries (429, 408, 5xx), the same call goes to its fallback, then to that one's fallback, and so
// on. A call that already streamed text to the client is never repeated, and a chain is never walked twice.
public sealed partial class NodeCaller(INineRouterService router, INodeConcurrencyLimiter limiter, INodeService nodeService) : INodeCaller
{
}
