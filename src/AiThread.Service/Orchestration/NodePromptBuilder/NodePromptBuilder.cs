using AiThread.Service.Interfaces;

namespace AiThread.Service.Orchestration;

// System prompt of a node: its role prompt, additional instructions and attached skills (see AgentPrompts.ForNode).
// The node must come from INodeService, which loads its skills.
public sealed partial class NodePromptBuilder(ISettingService settingService, ISkillPaths paths) : INodePromptBuilder
{
}
