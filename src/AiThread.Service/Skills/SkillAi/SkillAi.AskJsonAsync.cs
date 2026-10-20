using System.Text.Json.Nodes;
using AiThread.Service.Models;

namespace AiThread.Service.Skills;

public sealed partial class SkillAi
{
    // One JSON-only call; Json is null when the answer holds no readable JSON object.
    public async Task<(JsonNode? Json, string Model, int Tokens)> AskJsonAsync(string systemPrompt, string input, int maxTokens, CancellationToken cancellationToken)
    {
        var model = await ResolveModelAsync(cancellationToken);
        var result = await router.ChatAsync(new ChatCompletionRequest
        {
            Model = model,
            Messages = [ChatMessages.System(systemPrompt), ChatMessages.User(input)],
            Temperature = 0.1,
            MaxTokens = maxTokens
        }, cancellationToken: cancellationToken);
        return (ParseObject(result.Content), model, result.TotalTokens);
    }
}
