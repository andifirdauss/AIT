using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ContextKeeper
{
    // Author describes who wrote the pack, for the activity log. fallback builds a pack without a model.
    public async Task<(string Pack, string Author)> WritePackAsync(
        string input, Func<string> fallback, Guid? threadId, Guid? projectId, CancellationToken cancellationToken)
    {
        // A dedicated node when there is one, otherwise the wrapper (also a cheap 9router model).
        var node = (await nodeService.GetActiveAsync(NodeRole.ContextKeeper, cancellationToken)).FirstOrDefault()
                   ?? (await nodeService.GetActiveAsync(NodeRole.Wrapper, cancellationToken)).FirstOrDefault();
        if (node is null) return (fallback(), Lang.T("ringkasan otomatis (tidak ada node Context Keeper/Wrapper)", "automatic summary (no Context Keeper/Wrapper node)"));

        try
        {
            var reply = await caller.ChatAsync(node, async candidate => new ChatCompletionRequest
            {
                Model = candidate.Model,
                Messages =
                [
                    ChatMessages.System(await prompts.BuildAsync(AgentPrompts.ContextKeeper, candidate, cancellationToken)),
                    ChatMessages.User(input)
                ],
                Temperature = candidate.Temperature ?? 0.2,
                MaxTokens = candidate.MaxTokens
            }, cancellationToken, onSwitch: s =>
            {
                activityLog.Warn("context", Lang.T($"Context Keeper `{s.From.Model}` tidak tersedia ({s.Reason}), beralih ke `{s.To.Model}`.", $"Context Keeper `{s.From.Model}` not available ({s.Reason}), switching to `{s.To.Model}`."), threadId, projectId);
                return Task.CompletedTask;
            });

            if (ContextPackBuilder.Clean(reply.Result.Content) is { } pack) return (pack, $"{reply.Node.Model}, {reply.Result.TotalTokens} token");
            activityLog.Warn("context", Lang.T($"Context Keeper `{reply.Node.Model}` tidak mengembalikan ringkasan; memakai ringkasan otomatis.", $"Context Keeper `{reply.Node.Model}` returned no summary; using an automatic summary."), threadId, projectId);
        }
        catch (NineRouterException ex)
        {
            activityLog.Warn("context", Lang.T($"Context Keeper `{node.Model}` gagal ({ex.Message}); memakai ringkasan otomatis.", $"Context Keeper `{node.Model}` failed ({ex.Message}); using an automatic summary."), threadId, projectId);
        }
        return (fallback(), Lang.T("ringkasan otomatis", "automatic summary"));
    }
}
