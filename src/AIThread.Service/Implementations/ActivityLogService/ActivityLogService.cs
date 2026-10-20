using AIThread.Data.Context;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class ActivityLogService(
    IEventBus eventBus,
    IActivityLogQueue queue,
    IDbContextFactory<AIThreadDbContext> dbFactory,
    ILogger<ActivityLogService> logger) : IActivityLogService
{
}
