using System.Text.Json.Nodes;
using AIThread.Common.Entities;

namespace AIThread.Service.Interfaces;

public interface IMemoryAi
{
    Task<AiNode?> PickNodeAsync(CancellationToken cancellationToken);

    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);

    Task<(JsonNode? Json, int Tokens)> AskJsonAsync(string system, string input, int maxTokens, CancellationToken cancellationToken);
}
