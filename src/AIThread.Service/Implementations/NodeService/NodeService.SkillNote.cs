using AIThread.Common.Entities;

namespace AIThread.Service.Implementations;

public sealed partial class NodeService
{
    private static string SkillNote(AiNode node) => node.Skills.Count == 0 ? string.Empty : $", {node.Skills.Count} skill";
}
