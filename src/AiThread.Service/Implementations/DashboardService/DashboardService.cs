using AiThread.Data.Cache;
using AiThread.Data.Context;
using AiThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

// Every part is computed defensively: the dashboard must render even when 9router, Redis or SQL is down.
public sealed partial class DashboardService(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    INineRouterService router,
    INodeService nodeService,
    IMemoryService memoryService,
    IShortMemoryStore shortMemoryStore,
    IThreadRegistry registry,
    ILogger<DashboardService> logger) : IDashboardService
{
}
