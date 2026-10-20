using AiThread.Data.Context;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed partial class SkillService(
    IDbContextFactory<AiThreadDbContext> dbFactory,
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
