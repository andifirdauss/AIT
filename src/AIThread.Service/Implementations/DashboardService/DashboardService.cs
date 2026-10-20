using AIThread.Data.Cache;
using AIThread.Data.Context;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

// Every part is computed defensively: the dashboard must render even when 9router, Redis or SQL is down.
public sealed partial class DashboardService(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    INineRouterService router,
    INodeService nodeService,
    IMemoryService memoryService,
    IShortMemoryStore shortMemoryStore,
    IThreadRegistry registry,
    ILogger<DashboardService> logger) : IDashboardService
{
}
