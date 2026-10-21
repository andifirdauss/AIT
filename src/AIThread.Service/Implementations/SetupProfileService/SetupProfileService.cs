using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Implementations;

// Import first plans every selected section against this machine's state (the change texts of the dry run), then
// runs the planned actions in order: router, orchestration, memory, skills, nodes, business memory. Nodes match by
// name and role; fallbacks and tool delegations are linked by name once every node of the file exists.
public sealed partial class SetupProfileService(
    ISettingService settingService,
    INineRouterService router,
    INodeService nodeService,
    IMemoryStorageService memoryStorage,
    IBusinessMemoryService businessMemory,
    ISkillService skillService,
    IActivityLogService activityLog) : ISetupProfileService
{
    private static readonly IReadOnlyDictionary<NodeRole, string> RoleLabels = new Dictionary<NodeRole, string>
    {
        [NodeRole.Planner] = "Planner",
        [NodeRole.ToolsNode] = "Tools Node",
        [NodeRole.Executor] = "Executor",
        [NodeRole.Reviewer] = "Reviewer",
        [NodeRole.Wrapper] = "Wrapper",
        [NodeRole.ContextKeeper] = "Context Keeper",
        [NodeRole.Tester] = "Tester"
    };

    private static readonly IReadOnlyDictionary<SetupProfileSection, string> SectionLabels = new Dictionary<SetupProfileSection, string>
    {
        [SetupProfileSection.Router] = "Koneksi 9router",
        [SetupProfileSection.Orchestration] = "Proxy & orkestrasi",
        [SetupProfileSection.Nodes] = "Node",
        [SetupProfileSection.Memory] = "Penyimpanan memory",
        [SetupProfileSection.Business] = "Business memory",
        [SetupProfileSection.Skills] = "Skill"
    };

    private sealed class ImportPlan
    {
        private readonly HashSet<string> _warned = new(StringComparer.Ordinal);

        public List<SetupProfileChangeDto> Changes { get; } = [];
        public List<SetupProfileSecretDto> Secrets { get; } = [];
        public List<string> Warnings { get; } = [];
        public List<(SetupProfileSection Section, Func<Task> Apply)> Actions { get; } = [];

        public void Change(SetupProfileSection section, string text) => Changes.Add(new SetupProfileChangeDto(section, text));

        // The same skill missing for several nodes is reported once.
        public void Warn(string text)
        {
            if (_warned.Add(text)) Warnings.Add(text);
        }
    }

    // A node of the file with its links checked against the file and this machine. Existing = the node it updates.
    private sealed record ResolvedNode(
        SetupProfileNode Node,
        NodeDto? Existing,
        string? Fallback,
        IReadOnlyList<SetupProfileDelegation> Delegations,
        IReadOnlyList<Guid> SkillIds,
        bool Changed);

    private sealed record SkillEntry(Guid Id, string Name, string SourceUrl);
}
