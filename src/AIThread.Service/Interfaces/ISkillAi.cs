using System.Text.Json.Nodes;

namespace AIThread.Service.Interfaces;

public interface ISkillAi
{
    Task<string> ResolveModelAsync(CancellationToken cancellationToken);

    Task<(JsonNode? Json, string Model, int Tokens)> AskJsonAsync(string systemPrompt, string input, int maxTokens, CancellationToken cancellationToken);
}
