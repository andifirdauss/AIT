using AIThread.Data.Context;
using AIThread.Data.Memory;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

// unitTests: the store of the unit test catalog, for the warning that its failures go without a tester's triage.
public sealed partial class NodeService(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    INineRouterService router,
    IEventBus eventBus,
    IActivityLogService activityLog,
    LongMemoryStore unitTests) : INodeService
{
    public const int MaxSkillsPerNode = 10;
}
