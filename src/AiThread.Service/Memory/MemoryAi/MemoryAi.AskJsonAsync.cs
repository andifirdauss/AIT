using System.Text.Json.Nodes;
using AiThread.Service.Models;
using AiThread.Service.Skills;

namespace AiThread.Service.Memory;

public sealed partial class MemoryAi
{
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
}
