namespace AiThread.Common.Entities;

// A skill attached to a node. Its content is put in the node's system prompt, in Order.
public class NodeSkill
{
    public Guid AiNodeId { get; set; }
    public AiNode? AiNode { get; set; }

    public Guid SkillId { get; set; }
    public Skill? Skill { get; set; }

    public int Order { get; set; }
}
