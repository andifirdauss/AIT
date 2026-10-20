using AIThread.Data.Context;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class TelegramService(
    ISettingService settingService,
    ITelegramApi api,
    ITelegramBotRuntime runtime,
    IDbContextFactory<AIThreadDbContext> dbFactory,
    IEventBus eventBus,
    IActivityLogService activityLog) : ITelegramService
{
}
