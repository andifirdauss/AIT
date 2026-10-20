using AiThread.Data.Context;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed partial class ActivityLogService(
    IEventBus eventBus,
    IActivityLogQueue queue,
    IDbContextFactory<AiThreadDbContext> dbFactory,
    ILogger<ActivityLogService> logger) : IActivityLogService
{
}
