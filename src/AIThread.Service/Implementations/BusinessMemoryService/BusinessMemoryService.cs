using AIThread.Data.Context;
using AIThread.Data.Memory;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class BusinessMemoryService(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    BusinessMemoryStore store,
    IBusinessPageStore pages,
    IBusinessDigestRunner runner,
    ISettingService settingService,
    IEventBus eventBus,
    IActivityLogService activityLog) : IBusinessMemoryService
{
    private const int PlannerContentMaxChars = 15_000;
}
