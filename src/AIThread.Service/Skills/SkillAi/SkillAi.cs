using AIThread.Service.Interfaces;

namespace AIThread.Service.Skills;

// The model the skill orchestrator talks to: the one chosen in Setting → Skill, else the preferred active planner.
public sealed partial class SkillAi(INineRouterService router, ISettingService settingService, INodeService nodeService) : ISkillAi
{
}
