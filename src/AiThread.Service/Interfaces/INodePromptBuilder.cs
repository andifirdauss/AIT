using AiThread.Common.Entities;

namespace AiThread.Service.Interfaces;

public interface INodePromptBuilder
{
    Task<string> BuildAsync(string basePrompt, AiNode node, CancellationToken cancellationToken, bool canReadFiles = false);
}
