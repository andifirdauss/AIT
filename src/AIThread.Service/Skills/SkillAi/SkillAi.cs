using AIThread.Service.Interfaces;

namespace AIThread.Service.Skills;

// The model the skill orchestrator talks to: the one chosen in the Skill menu (or the Node menu), else the preferred active planner.
public sealed partial class SkillAi(INineRouterService router, ISettingService settingService, INodeService nodeService) : ISkillAi
{
}
