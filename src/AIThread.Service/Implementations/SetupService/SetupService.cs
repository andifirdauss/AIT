using AIThread.Common.Enums;
using AIThread.Common.Options;
using AIThread.Data.Cache;
using AIThread.Data.Context;
using AIThread.Service.Interfaces;
using AIThread.Service.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIThread.Service.Implementations;

// Every part of the status is computed defensively, like the dashboard: the Setup page must render while the database,
// 9router or a CLI config file is unavailable, since that is exactly what it reports.
public sealed partial class SetupService(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    INineRouterService router,
    INodeService nodeService,
    ISettingService settingService,
    IShortMemoryStore shortMemoryStore,
    IMemoryStorageService memoryStorage,
    IEnumerable<ICliIntegration> integrations,
    IActivityLogService activityLog,
    IOptions<AIThreadOptions> options,
    ILogger<SetupService> logger) : ISetupService
{
    // Preset nodes are recognized by name and role, so running the preset again updates them instead of adding more.
    private static readonly IReadOnlyDictionary<NodeRole, string> PresetNames = new Dictionary<NodeRole, string>
    {
        [NodeRole.Planner] = "AIT Planner",
        [NodeRole.ToolsNode] = "AIT Tools Node",
        [NodeRole.Executor] = "AIT Executor",
        [NodeRole.Reviewer] = "AIT Reviewer",
        [NodeRole.Tester] = "AIT Tester",
        [NodeRole.Wrapper] = "AIT Wrapper",
        [NodeRole.ContextKeeper] = "AIT Context Keeper"
    };

    // Below the default 100 of nodes added by hand, so the models picked in Setup are the ones used.
    private const int PresetPriority = 10;

    // What the Node menu fills in for a new node of each role (wwwroot/js/pages/nodes.js).
    private static readonly IReadOnlyDictionary<NodeRole, RoleDefault> RoleDefaults = new Dictionary<NodeRole, RoleDefault>
    {
        [NodeRole.Planner] = new(16384, 2, 0.2),
        [NodeRole.ToolsNode] = new(4096, 4, 0.1),
        [NodeRole.Executor] = new(8192, 6, 0.1),
        [NodeRole.Reviewer] = new(4096, 2, 0.1),
        [NodeRole.Tester] = new(4096, 1, 0.1),
        [NodeRole.Wrapper] = new(4096, 4, 0.3),
        [NodeRole.ContextKeeper] = new(4096, 2, 0.2)
    };

    // The planner tools with the largest output (the Node menu's advice), handed to the preset tools node.
    private static readonly string[] DelegatedTools = [PlannerTools.ReadFile, PlannerTools.SearchText, PlannerTools.FindReferences];

    private sealed record RoleDefault(int MaxTokens, int MaxConcurrency, double Temperature);
}
