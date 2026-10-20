using System.Text.Json.Nodes;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;
using AIThread.Service.Skills;
using AIThread.Service.Workspace.Structure;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.CodeCalls;

public sealed partial class FunctionCallScanner
{
    private async Task<List<CodeFunctionScan>> AskAsync(AiNode node, string path, string language, string[] lines, FileOutline outline,
        StructureSymbolIndex symbols, List<Pending> batch, CancellationToken cancellationToken)
    {
        string? model = node.Model;
        var tokens = 0;
        JsonNode? json;
        try
        {
            var reply = await caller.ChatAsync(node, candidate => Task.FromResult(new ChatCompletionRequest
            {
                Model = candidate.Model,
                Messages = [ChatMessages.System(AgentPrompts.FunctionCalls), ChatMessages.User(Input(path, language, lines, batch))],
                Temperature = 0.1,
                MaxTokens = Math.Min(candidate.MaxTokens, MaxTokens)
            }), cancellationToken);
            model = reply.Node.Model;
            tokens = reply.Result.TotalTokens;
            json = SkillAi.ParseObject(reply.Result.Content);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Tools node could not read the calls of {Count} function(s) in {Path}", batch.Count, path);
            return batch.Select(p => Scan(path, p, FunctionScanStatus.Failed, model, 0, Truncate(ex.Message, 1000), [])).ToList();
        }

        var answers = (json?["functions"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(f => SkillAi.Text(f["id"]) is not null)
            .GroupBy(f => SkillAi.Text(f["id"])!)
            .ToDictionary(g => g.Key, g => g.First());
        var share = batch.Count == 0 ? 0 : tokens / batch.Count;
        var scans = new List<CodeFunctionScan>();
        for (var i = 0; i < batch.Count; i++)
        {
            var pending = batch[i];
            if (!answers.TryGetValue($"F{i + 1}", out var answer))
            {
                scans.Add(Scan(path, pending, FunctionScanStatus.Failed, model, share, "The tools node did not answer for this function.", []));
                continue;
            }
            var calls = Calls(answer, pending.Span, path, lines, outline, symbols);
            scans.Add(Scan(path, pending, FunctionScanStatus.Done, model, share, null, calls));
        }
        return scans;
    }
}
