using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Service.Orchestration;

namespace AiThread.Service.Implementations;

public sealed partial class NodeService
{
    private static NodeDto ToDto(AiNode n, IReadOnlyDictionary<Guid, string> names) => new(
        n.Id, n.Name, n.Role, n.Model, n.Temperature, n.MaxTokens, n.MaxConcurrency,
        n.Priority, n.SystemPrompt, n.IsActive, n.CreatedAt, n.UpdatedAt,
        n.Skills.OrderBy(s => s.Order).Where(s => s.Skill is not null)
            .Select(s => new NodeSkillDto(s.SkillId, s.Skill!.Name, s.Skill.Description, s.Skill.SkillSource?.Name ?? string.Empty))
            .ToList(),
        n.FallbackNodeId,
        n.FallbackNodeId is { } id && names.TryGetValue(id, out var name) ? name : null,
        n.ToolDelegations
            .OrderBy(d => PlannerTools.Delegable.IndexOf(d.Tool))
            .Select(d => new ToolDelegationDto(d.Tool, d.ToolsNodeId, d.ToolsNode?.Name ?? string.Empty, d.ToolsNode?.IsActive ?? false))
            .ToList());
}
