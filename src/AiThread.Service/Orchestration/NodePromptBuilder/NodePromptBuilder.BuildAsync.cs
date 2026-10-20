using AiThread.Common.Entities;

namespace AiThread.Service.Orchestration;

public sealed partial class NodePromptBuilder
{
    public async Task<string> BuildAsync(string basePrompt, AiNode node, CancellationToken cancellationToken, bool canReadFiles = false)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        return AgentPrompts.ForNode(basePrompt, node, settings.SkillPromptMaxChars, paths.Root, canReadFiles);
    }
}
