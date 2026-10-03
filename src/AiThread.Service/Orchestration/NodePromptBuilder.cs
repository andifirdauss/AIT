using AiThread.Common.Entities;
using AiThread.Service.Interfaces;
using AiThread.Service.Skills;

namespace AiThread.Service.Orchestration;

// System prompt of a node: its role prompt, additional instructions and attached skills (see AgentPrompts.ForNode).
// The node must come from INodeService, which loads its skills.
public sealed class NodePromptBuilder(ISettingService settingService, SkillPaths paths)
{
    public async Task<string> BuildAsync(string basePrompt, AiNode node, CancellationToken cancellationToken, bool canReadFiles = false)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        return AgentPrompts.ForNode(basePrompt, node, settings.SkillPromptMaxChars, paths.Root, canReadFiles);
    }
}
