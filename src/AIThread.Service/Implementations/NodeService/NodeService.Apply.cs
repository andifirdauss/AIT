using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;

namespace AIThread.Service.Implementations;

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
        // Only a planner tests its plans or reads for itself; null keeps what the node has.
        if (request.AutoTesting is { } autoTesting) node.AutoTesting = autoTesting;
        if (request.DirectRead is { } directRead) node.DirectRead = directRead;
        if (request.DirectReadPercent is { } percent) node.DirectReadPercent = Math.Clamp(percent, 0, 100);
        if (node.Role != NodeRole.Planner)
        {
            node.AutoTesting = false;
            node.DirectRead = false;
        }
    }
}
