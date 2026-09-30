using AiThread.Common.Enums;

namespace AiThread.Common.Entities;

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
}
