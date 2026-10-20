using AiThread.Data.Context;
using AiThread.Data.Memory;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class BusinessMemoryService(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    BusinessMemoryStore store,
    IBusinessPageStore pages,
    IBusinessDigestRunner runner,
    ISettingService settingService,
    IEventBus eventBus,
    IActivityLogService activityLog) : IBusinessMemoryService
{
    private const int PlannerContentMaxChars = 15_000;
}
