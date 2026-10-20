using AIThread.Data.Context;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class NodeService(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    INineRouterService router,
    IEventBus eventBus,
    IActivityLogService activityLog) : INodeService
{
    public const int MaxSkillsPerNode = 10;
}
