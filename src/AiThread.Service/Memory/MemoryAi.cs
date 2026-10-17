using System.Text.Json.Nodes;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Orchestration;
using AiThread.Service.Skills;

namespace AiThread.Service.Memory;

public sealed class MemoryAiUnavailableException() : Exception("No active Tools node, Wrapper or Planner to label memory with.");

// The model calls of the memory catalog (code map, relabeling, duplicate modules): the first active tools node (cheap),
// else the wrapper, else the planner, each with its own fallback chain and concurrency limit.
public sealed class MemoryAi(NodeCaller caller, INodeService nodeService)
{
    private static readonly NodeRole[] Roles = [NodeRole.ToolsNode, NodeRole.Wrapper, NodeRole.Planner];

    public async Task<AiNode?> PickNodeAsync(CancellationToken cancellationToken)
    {
        foreach (var role in Roles)
            if ((await nodeService.GetActiveAsync(role, cancellationToken)).FirstOrDefault() is { } node) return node;
        return null;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => await PickNodeAsync(cancellationToken) is not null;

    // Null Json when the model did not answer a JSON object.
    public async Task<(JsonNode? Json, int Tokens)> AskJsonAsync(string system, string input, int maxTokens, CancellationToken cancellationToken)
    {
        var node = await PickNodeAsync(cancellationToken) ?? throw new MemoryAiUnavailableException();
        var reply = await caller.ChatAsync(node, candidate => Task.FromResult(new ChatCompletionRequest
        {
            Model = candidate.Model,
            Messages = [ChatMessages.System(system), ChatMessages.User(input)],
            Temperature = 0.1,
            MaxTokens = Math.Min(candidate.MaxTokens, maxTokens)
        }), cancellationToken);
        return (SkillAi.ParseObject(reply.Result.Content), reply.Result.TotalTokens);
    }

    public static IReadOnlyList<string> Strings(JsonNode? node) => node switch
    {
        JsonArray array => array.Select(SkillAi.Text).OfType<string>().ToList(),
        JsonValue => SkillAi.Text(node) is { } text ? text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [],
        _ => []
    };
}
