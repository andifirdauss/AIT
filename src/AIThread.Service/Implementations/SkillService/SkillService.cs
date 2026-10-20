using AIThread.Data.Context;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class SkillService(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    ISettingService settingService,
    ISkillPaths paths,
    ISkillImportQueue queue,
    ISkillImportProgress progress,
    IEventBus eventBus,
    IActivityLogService activityLog,
    ILogger<SkillService> logger) : ISkillService
{
    private const int MaxDetailFiles = 200;
}
