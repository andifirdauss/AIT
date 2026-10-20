using AiThread.Common.DTOs;
using AiThread.Common.Entities;

namespace AiThread.Service.Implementations;

public sealed partial class NodeService
{
    private static void Apply(AiNode node, SaveNodeRequest request)
    {
        node.Name = request.Name.Trim();
        node.Role = request.Role;
        node.Model = request.Model.Trim();
        node.Temperature = request.Temperature;
        node.MaxTokens = request.MaxTokens;
        node.MaxConcurrency = request.MaxConcurrency;
        node.Priority = request.Priority;
        node.SystemPrompt = string.IsNullOrWhiteSpace(request.SystemPrompt) ? null : request.SystemPrompt.Trim();
        node.IsActive = request.IsActive;
        node.FallbackNodeId = request.FallbackNodeId;
    }
}
