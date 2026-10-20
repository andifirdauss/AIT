using AIThread.Common.Enums;

namespace AIThread.Common.Entities;

// An AI worker (planner / executor / wrapper / context keeper) backed by a 9router model or combo.
public class AiNode : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public NodeRole Role { get; set; }

    // A 9router model/combo id.
    public string Model { get; set; } = string.Empty;
    public double? Temperature { get; set; }
    public int MaxTokens { get; set; } = 8192;

    // Maximum simultaneous calls to this node across all running threads.
    public int MaxConcurrency { get; set; } = 4;

    // Lower value = preferred when several nodes share a role.
    public int Priority { get; set; } = 100;

    // Extra instructions appended to the built-in role prompt.
    public string? SystemPrompt { get; set; }

    public bool IsActive { get; set; } = true;

    // Optional node of the same role that takes over a call this node cannot answer (rate limit, overload, timeout),
    // after 9router's own retries. It may be inactive: a pure backup that gets no work of its own. Its own fallback
    // forms a chain; cycles are rejected when saving.
    public Guid? FallbackNodeId { get; set; }

    // Skills from the skill store, put in the system prompt after the additional instructions.
    public List<NodeSkill> Skills { get; set; } = [];

    // Planner only: tools handed to tools nodes (role ToolsNode).
    public List<PlannerToolDelegation> ToolDelegations { get; set; } = [];
}
