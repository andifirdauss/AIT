using System.Text.Json.Nodes;
using AiThread.Common.Entities;

namespace AiThread.Service.Interfaces;

public interface IMemoryAi
{
    Task<AiNode?> PickNodeAsync(CancellationToken cancellationToken);

    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);

    Task<(JsonNode? Json, int Tokens)> AskJsonAsync(string system, string input, int maxTokens, CancellationToken cancellationToken);
}
