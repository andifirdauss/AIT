using AiThread.Data.Context;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class TelegramService(
    ISettingService settingService,
    ITelegramApi api,
    ITelegramBotRuntime runtime,
    IDbContextFactory<AiThreadDbContext> dbFactory,
    IEventBus eventBus,
    IActivityLogService activityLog) : ITelegramService
{
}
