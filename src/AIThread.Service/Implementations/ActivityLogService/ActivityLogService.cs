using AIThread.Data.Context;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

// Messages are written with their secrets masked (SecretGuard): the log is stored and shown live on every page.
public sealed partial class ActivityLogService(
    IEventBus eventBus,
    IActivityLogQueue queue,
    IDbContextFactory<AIThreadDbContext> dbFactory,
    ISecretGuard secrets,
    ILogger<ActivityLogService> logger) : IActivityLogService
{
}
