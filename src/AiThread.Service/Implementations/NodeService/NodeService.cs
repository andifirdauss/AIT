using AiThread.Data.Context;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class NodeService(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    INineRouterService router,
    IEventBus eventBus,
    IActivityLogService activityLog) : INodeService
{
    public const int MaxSkillsPerNode = 10;
}
